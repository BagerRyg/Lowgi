using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace LowgiPrimitives.IPC
{
    public static class MessagePipeHelper
    {
        public static void AddLowgiMessagePipe(this IServiceCollection services, bool hostAsServer = false, bool enableInterprocess = true)
        {
            services.AddMessagePipe(options =>
            {
                options.EnableCaptureStackTrace = false;
            });

            if (!enableInterprocess)
            {
                return;
            }

            services.AddMessagePipeNamedPipeInterprocess("Lowgi", config =>
            {
                config.HostAsServer = hostAsServer;
            });
        }
    }
}
