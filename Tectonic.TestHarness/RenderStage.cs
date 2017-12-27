using SharpVk;

namespace Tectonic
{
    public abstract class RenderStage
    {
        public virtual void Initialise(Device device, VulkanBufferManager bufferManager)
        {
        }

        public abstract void Bind(Device device, RenderPass renderPass, CommandBuffer commandBuffer, Extent2D targetExtent);

        public virtual void Update()
        {
        }
    }
}
