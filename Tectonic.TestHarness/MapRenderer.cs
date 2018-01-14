using GlmSharp;
using System.Linq;

namespace Tectonic
{
    public class MapRenderer
        : GameService, IUpdatable
    {
        private readonly IUpdateLoopService updateService;
        private SpriteStage tileStage;
        private SpriteStage wallStage;
        private bool needsUpdate;

        private const int layerCount = 1;

        private const int sideSize = 8;

        public MapRenderer(IUpdateLoopService updateService)
        {
            this.updateService = updateService;
        }

        public override void Start()
        {
            this.updateService.Register(this, UpdateStage.PreRender);
        }

        public void Bind(VulkanDeviceService vulkanService)
        {
            this.tileStage = vulkanService.CreateStage<SpriteStage>((sideSize * sideSize) * 16);
            this.wallStage = vulkanService.CreateStage<SpriteStage>((sideSize * sideSize) * 16);

            this.needsUpdate = true;
        }

        public void Update()
        {
            if (this.needsUpdate)
            {
                ivec2 GetAtlas(int x, int y)
                {
                    if (x == (sideSize - 1))
                    {
                        if (y == (sideSize - 1))
                        {
                            return new ivec2(0, 0);
                        }
                        else
                        {
                            return new ivec2(0, 1);
                        }
                    }
                    else if (x == 0)
                    {
                        if (y == (sideSize - 1))
                        {
                            return new ivec2(1, 0);
                        }
                        else if (y == 0)
                        {
                            return new ivec2(1, 1);
                        }
                        else
                        {
                            return new ivec2(0, 1);
                        }
                    }
                    else
                    {
                        if (y == (sideSize - 1) || y == 0)
                        {
                            return new ivec2(1, 0);
                        }
                        else
                        {
                            return new ivec2(0, 0);
                        }
                    }
                }

                this.tileStage.SetInstanceCount(sideSize * sideSize * layerCount);
                this.wallStage.SetInstanceCount(sideSize * sideSize * layerCount);
                
                this.tileStage.SetInstances(Enumerable.Range(0, sideSize).SelectMany(x => Enumerable.Range(0, sideSize).Select(y => (x, y, z: 0))).Select(coord =>
                        new SpriteStage.SpriteData
                        {
                            AtlasPosition = (coord.x == (sideSize - 1) || coord.y == (sideSize - 1)) ? new ivec2(0, 0) : new ivec2(2, 0),
                            Transform = mat4.Translate(((coord.x - coord.y) * 0.5f) - 0.5f, ((coord.x + coord.y) * 0.25f) - (0.5f * coord.z), 0)
                        }).ToArray());

                this.wallStage.SetInstances(Enumerable.Range(0, sideSize).SelectMany(x => Enumerable.Range(0, sideSize).Select(y => (x, y, z: 0))).Select(coord =>
                        new SpriteStage.SpriteData
                        {
                            AtlasPosition = GetAtlas(coord.x, coord.y),
                            Transform = mat4.Translate(((coord.x - coord.y) * 0.5f) - 0.5f, ((coord.x + coord.y) * 0.25f) - (0.5f * coord.z), 0)
                        }).ToArray());

                this.needsUpdate = false;
            }
        }
    }
}