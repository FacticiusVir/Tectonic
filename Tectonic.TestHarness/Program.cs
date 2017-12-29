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

            vulkanService.CreateStage<ClearStage>();
            vulkanService.CreateStage<QuadStage>();

            game.Start();

            while (game.RunState == GameRunState.Running)
            {
                updateLoop.RunFrame();
            }

            game.Stop();
        }
    }
}
