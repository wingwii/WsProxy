using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace server
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            app.UseWebSockets();

            app.Use(async (ctx, next) =>
            {
                if (ctx.WebSockets.IsWebSocketRequest)
                {
                    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                    await HandleWsProxy(ctx, ws);
                }
                else
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                    //await next();
                }

            });
        }

        private async Task HandleWsProxy(HttpContext ctx, WebSocket ws)
        {
            try
            {
                var proxy = new WsProxy(ctx, ws);
                await proxy.Run();
            }
            catch (Exception) { }

            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, System.Threading.CancellationToken.None);
            }
            catch (Exception) { }

            try { ws.Dispose(); }
            catch (Exception) { }
        }

    }
}
