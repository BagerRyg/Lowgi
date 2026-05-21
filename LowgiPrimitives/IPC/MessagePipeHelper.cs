using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace LowgiPrimitives.IPC
{
    public static class MessagePipeHelper
    {
        public static void AddLowgiMessagePipe(this IServiceCollection services, bool hostAsServer = false)
        {
            services.AddMessagePipe(options =>
            {
                options.EnableCaptureStackTrace = true;
            });

            services.AddMessagePipeNamedPipeInterprocess("Lowgi", config =>
            {
                config.HostAsServer = hostAsServer;
            });
        }
    }
}
