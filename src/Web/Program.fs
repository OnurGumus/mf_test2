module Web

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open DotLiquid
open Yarp.ReverseProxy.Forwarder
open System.Net.Http
open FSharp.Control.Tasks
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Microsoft.AspNetCore.Http.Features
open System.Net
open System.Text

// ---------------------------------
// Web app
// ---------------------------------

let authHandler (name: string) (context: HttpContext) : HttpHandler =
    let cookieColl = context.Request.Cookies
    let baseAddress = Uri("http://auth:5010")
    use handler = new HttpClientHandler()

    let httpClient =
        new HttpClient(handler, BaseAddress = baseAddress)

    let header =
        httpClient
            .GetAsync(
                "/auth/header"
            )
            .Result
            .Content
            .ReadAsStringAsync()
            .Result

    let body =
        if context.Request.Method = "GET" then
            let result = httpClient.GetAsync("/auth/" + name).Result

            match result.Headers.TryGetValues("Set-Cookie") with
            | true, cookie -> context.Response.Headers.Add("Set-Cookie", StringValues(cookie |> Seq.toArray))
            | _ -> ()  

            result.Content
                .ReadAsStringAsync()
                .Result
        else
            let cookies =
                match context.Request.Headers.TryGetValue("Cookie") with
                | true, cookies -> cookies
                | _ -> StringValues.Empty

            let request =
                new HttpRequestMessage(RequestUri = Uri("http://auth:5010/auth/" + name), 
                    Method = HttpMethod.Post)
            use streamReader = new StreamReader(context.Request.Body)
            let str = streamReader.ReadToEndAsync().Result
            let body = new StringContent(str,  Encoding.UTF8, context.Request.ContentType )
            request.Content <- body
            cookies
            |> Seq.iter (fun x -> request.Headers.Add("Cookie", x))

            let res = httpClient.SendAsync(request).Result
          
            match res.Headers.TryGetValues("Set-Cookie") with
            | true, cookie -> context.Response.Headers.Add("Set-Cookie", StringValues(cookie |> Seq.toArray))
            | _ -> ()
            //   let cookies = cookieContainer.GetCookies(baseAddress)

            //  cookies|> Seq.iter (fun x -> context.Response.Cookies.Append(x.Name,x.Value))
            res.Content.ReadAsStringAsync().Result


    let model = {| Header = header; Body = body |}
    dotLiquidHtmlTemplate "Views/Index.html" model

let appHandler (name: string) =
    let httpClient = new HttpClient()

    let header =
        httpClient
            .GetAsync(
                "http://auth:5010/auth/header"
            )
            .Result
            .Content
            .ReadAsStringAsync()
            .Result

    let body =
        httpClient
            .GetAsync(
                "http://app:5020/app/" + name
            )
            .Result
            .Content
            .ReadAsStringAsync()
            .Result

    let model = {| Header = header; Body = body |}
    dotLiquidHtmlTemplate "Views/Index.html" model

let getAuth () = ()
//  let client = HttpClient

let POST_GET: HttpHandler = choose [ POST; GET ]

let webApp =
    choose [ POST_GET
             >=> choose [ routexp
                              "/auth/(.*)"
                              (fun s -> (fun x context -> (authHandler (s |> Seq.last) context) x context))
                          route "/auth"
                          >=> (fun x context -> (authHandler "" context) x context)
                          routexp "/app/(.*)" (Seq.last >> appHandler)
                          route "/app" >=> appHandler ""
                          route "/"
                          >=> (fun x context -> (authHandler "" context) x context) ]
             setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex: Exception) (logger: ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")

    clearResponse
    >=> setStatusCode 500
    >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder: CorsPolicyBuilder) =
    builder
        .WithOrigins("http://0.0.0.0:5000", "https://0.0.0.0:5001")
        .AllowAnyMethod()
        .AllowAnyHeader()
    |> ignore

type CustomTransformer() =
    inherit HttpTransformer()

    override _.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix) =
        let tr =
            base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix)

        task {
            do! tr
            proxyRequest.Headers.Host <- null
        }
        |> ValueTask

let configureApp (app: IApplicationBuilder) =
    let forwarder =
        app.ApplicationServices.GetService<IHttpForwarder>()

    let httpClient =
        new HttpMessageInvoker(
            new SocketsHttpHandler(
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                UseCookies = false
            )
        )

    let transformer = CustomTransformer() // or HttpTransformer.Default;

    let requestOptions = ForwarderRequestConfig()

    let env =
        app.ApplicationServices.GetService<IWebHostEnvironment>()

    let isFormPost (context: HttpContext) =
        context.Request.Headers.ContainsKey("content-type")
        && context.Request.Headers.["content-type"].[0]
            .Contains("form")

    let staticContent (context: HttpContext) =
        context.Request.Path.Value.Contains(".")

    let forwardPredicate (context: HttpContext) =
        (not (isFormPost context))
        && (staticContent (context)
            || context.Request.Method <> "GET"
            || context.Request.Headers.ContainsKey("Connection")
            || context.Request.ContentType = "application/json"
            || (context.Request.ContentType |> isNotNull
                && context.Request.ContentType.Contains("form")))

    app
        // .Use(fun context next ->
        //     task {
        //         let contentType = context.Request.ContentType

        //         if context.Request.Method = "GET"
        //            && contentType |> isNotNull
        //            && contentType.Contains "text/html" then
        //             let feature =
        //                 context.Features.Get<IHttpResponseBodyFeature>()

        //             let memStream = new MemoryStream()
        //             let x = StreamResponseBodyFeature(memStream)
        //             context.Features.Set<IHttpResponseBodyFeature>(x)
        //             do! next.Invoke()
        //             let streamReader = new StreamReader(memStream)
        //             memStream.Position <- 0L

        //             let! text = streamReader.ReadToEndAsync()

        //             let mem: ReadOnlyMemory<byte> = ReadOnlyMemory<_>(memStream.ToArray())

        //             let! _ = feature.Writer.WriteAsync(mem)
        //             context.Features.Set<IHttpResponseBodyFeature>(feature)
        //             return ()
        //         else
        //             do! next.Invoke()
        //             return ()
        //     }
        //     :> Task)
        .UseWhen(
            (Func<_, _>(staticContent)),
            fun a ->
                a.Use
                    (fun context next ->
                        task {
                            let cookies =
                                context.Request.Headers.["Cookie"] |> Seq.tryHead

                            match cookies with
                            | Some cookies ->
                                let httpClient = new HttpClient()
                                httpClient.DefaultRequestHeaders.Add("Cookie", cookies)

                                let! response = httpClient.GetAsync("http://auth:5010/api/resource/")

                                if response.IsSuccessStatusCode then
                                    let! result = response.Content.ReadAsStringAsync()
                                    context.Items.Add("user", result)
                                    context.Request.Headers.Add("user", StringValues(result))
                            | _ -> ()

                            do! next.Invoke()
                            return ()
                        }
                        :> Task)
                |> ignore
        )

        .UseWhen(
            (Func<_, _>(forwardPredicate)),
            fun a ->
                a
                    .UseRouting()
                    .UseEndpoints(fun endpoints ->
                        endpoints.Map(
                            "auth/{**catch}",
                            RequestDelegate
                                (fun httpContext ->
                                    upcast (task {
                                                let! error =
                                                    forwarder.SendAsync(
                                                        httpContext,
                                                        "http://auth:5010/",
                                                        httpClient,
                                                        requestOptions,
                                                        HttpTransformer.Default
                                                    )

                                                if (error <> ForwarderError.None) then
                                                    let errorFeature = httpContext.GetForwarderErrorFeature()
                                                    let ex = errorFeature.Exception
                                                    printf "%A" ex
                                                    ()

                                                return ()
                                            }))
                        )
                        |> ignore

                        endpoints.Map(
                            "app/{**catch}",
                            RequestDelegate
                                (fun httpContext ->
                                    upcast (task {
                                                let! _ =
                                                    forwarder.SendAsync(
                                                        httpContext,
                                                        "http://app:5020/",
                                                        httpClient,
                                                        requestOptions,
                                                        HttpTransformer.Default
                                                    )

                                                return ()
                                            }))
                        )
                        |> ignore)
                |> ignore
        )
    |> ignore

    (match env.IsDevelopment() with
     | true -> app.UseDeveloperExceptionPage()
     | false ->
         app
             .UseGiraffeErrorHandler(errorHandler)
             .UseHttpsRedirection())
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(webApp)

let configureServices (services: IServiceCollection) =
    services.AddHttpForwarder().AddCors().AddGiraffe()
    |> ignore

let configureLogging (builder: ILoggingBuilder) =
    builder.AddConsole().AddDebug() |> ignore

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot = Path.Combine(contentRoot, "wwwroot")

    Host
        .CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(fun webHostBuilder ->
            webHostBuilder
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .UseUrls("http://0.0.0.0:5000", "https://0.0.0.0:5001")
                .Configure(Action<IApplicationBuilder> configureApp)
                .ConfigureServices(configureServices)
                .ConfigureLogging(configureLogging)
            |> ignore)
        .Build()
        .Run()

    0
