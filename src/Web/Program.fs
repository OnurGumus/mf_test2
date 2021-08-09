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

// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name: string) =
    let greetings = sprintf "Hello %s, from Giraffe!" name
    let model = {| Text = greetings |}
    dotLiquidHtmlTemplate "Views/Index.html" model

let getAuth () = ()
//  let client = HttpClient
let webApp =
    choose [ GET
             >=> choose [ route "/" >=> indexHandler "world"
                          routef "/hello/%s" indexHandler ]
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

    app
        .Use(fun context next ->
            task {
                let cookies = context.Request.Headers.["Cookie"] |> Seq.tryHead
                match cookies with 
                | Some cookies ->
                    let httpClient = new HttpClient()
                    httpClient.DefaultRequestHeaders.Add("Cookie", cookies)
                    let! response = httpClient.GetAsync("http://auth:5010/api/resource/")
                    if response.IsSuccessStatusCode then
                        let! result = response.Content.ReadAsStringAsync()
                        context.Items.Add("user",  result)
                        printf "%A" result
                | _ -> ()
                do! next.Invoke()
                return ()
            }
            :> Task)
        .UseRouting()
        .UseEndpoints(fun endpoints ->
            endpoints.Map(
                "/{**catch-all}",
                RequestDelegate
                    (fun httpContext ->
                        upcast (task {
                                    let! _ =
                                        forwarder.SendAsync(
                                            httpContext,
                                            "http://auth:5010/",
                                            httpClient,
                                            requestOptions,
                                            transformer
                                        )
                                    return ()
                                }))
            )
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
