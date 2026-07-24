module GiraffeSsr.Sample.Program

// Minimal ASP.NET + Giraffe host for the Fuaran SSR sample. Port 14020
// (the "free for next F#-side sample" band — see the workspace port table).

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Giraffe

[<EntryPoint>]
let main _ =
    let builder = WebApplication.CreateBuilder()
    builder.Services.AddGiraffe() |> ignore
    let app = builder.Build()
    app.UseStaticFiles() |> ignore
    app.UseGiraffe App.webApp
    app.Run("http://localhost:14020")
    0
