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
open System.Net.Http
open FSharp.Control.Tasks
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Microsoft.AspNetCore.Http.Features

// ---------------------------------
// Web app
// ---------------------------------



let webApp =
    choose [ GET >=> 
                choose [
                    route "/my-component" >=> htmlFile "wwwroot/my-component.html"

                            ]
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
        .WithOrigins("http://0.0.0.0:5020", "https://0.0.0.0:5021")
        .AllowAnyMethod()
        .AllowAnyHeader()
    |> ignore


let configureApp (app: IApplicationBuilder) =
    app.UsePathBase(PathString "/app")|>ignore
    let env =
        app.ApplicationServices.GetService<IWebHostEnvironment>()




    (match env.IsDevelopment() with
     | true -> app.UseDeveloperExceptionPage()
     | false ->
         app
             .UseGiraffeErrorHandler(errorHandler))
          //   .UseHttpsRedirection())
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(webApp)

let configureServices (services: IServiceCollection) =
    services.AddCors().AddGiraffe() |> ignore

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
                .UseUrls("http://0.0.0.0:5020", "https://0.0.0.0:5021")
                .Configure(Action<IApplicationBuilder> configureApp)
                .ConfigureServices(configureServices)
                .ConfigureLogging(configureLogging)
            |> ignore)
        .Build()
        .Run()

    0
