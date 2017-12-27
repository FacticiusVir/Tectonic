using Microsoft.Extensions.DependencyInjection;
using SharpVk;

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
                                .Configure<GlfwOptions>(options =>
                                {
                                    options.Title = "Tectonic";
                                    options.InitialWidth = 1920;
                                    options.InitialHeight = 1080;
                                })
                                .BuildServiceProvider();

            var game = provider.CreateInstance<Game>();
            var updateLoop = provider.GetRequiredService<UpdateLoopService>();
            var vulkanService = provider.GetRequiredService<VulkanDeviceService>();

            game.Initialise();

            var clearStage = vulkanService.CreateStage<ClearStage>();
            clearStage.ClearRegion = new Rect2D(new Offset2D(100, 100), new Extent2D(200, 200));
            var clearStage2 = vulkanService.CreateStage<ClearStage>();
            clearStage2.ClearRegion = new Rect2D(new Offset2D(500, 300), new Extent2D(200, 200));

            game.Start();

            while (game.RunState == GameRunState.Running)
            {
                updateLoop.RunFrame();
            }

            game.Stop();
        }
    }
}
