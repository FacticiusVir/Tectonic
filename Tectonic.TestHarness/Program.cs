using GlmSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Tectonic
{
    public class Program
    {
        static void Main(string[] args)
        {
            var provider = new ServiceCollection()
                                .AddOptions()
                                .AddGameService<IUpdateLoopService, UpdateLoopService>()
                                .AddGameService<GlfwService>()
                                .AddGameService<VulkanDeviceService>()
                                .AddGameService<MapRenderer>()
                                .Configure<GlfwOptions>(options =>
                                {
                                    options.Title = "Tectonic";
                                    options.InitialWidth = 1280;
                                    options.InitialHeight = 1280;
                                })
                                .BuildServiceProvider();

            var game = provider.CreateInstance<Game>();
            var updateLoop = provider.GetRequiredService<UpdateLoopService>();
            var vulkanService = provider.GetRequiredService<VulkanDeviceService>();
            var mapRenderer = provider.GetRequiredService<MapRenderer>();

            game.Initialise();

            var clearStage = vulkanService.CreateStage<ClearStage>();
            clearStage.ClearColour = new vec4(0.25f, 0.25f, 0.25f, 0f);
            mapRenderer.Bind(vulkanService);

            game.Start();

            while (game.RunState == GameRunState.Running)
            {
                updateLoop.RunFrame();
            }

            game.Stop();
        }
    }
}
