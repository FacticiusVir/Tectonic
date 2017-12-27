using SharpVk;

namespace Tectonic
{
    public class ClearStage
        : RenderStage
    {
        public Rect2D? ClearRegion
        {
            get;
            set;
        }

        public override void Bind(Device device, RenderPass renderPass, CommandBuffer commandBuffer, Extent2D targetExtent)
        {
            var clearRegion = ClearRegion ?? new Rect2D(new Offset2D(), targetExtent);

            uint constrainedWidth = clearRegion.Extent.Width;

            if (clearRegion.Offset.X + clearRegion.Extent.Width > targetExtent.Width)
            {
                constrainedWidth = (uint)(targetExtent.Width - clearRegion.Offset.X);

                if (constrainedWidth < 0)
                {
                    constrainedWidth = 0;
                }
            }

            uint constrainedHeight = clearRegion.Extent.Height;

            if (clearRegion.Offset.Y + clearRegion.Extent.Height > targetExtent.Height)
            {
                constrainedHeight = (uint)(targetExtent.Height - clearRegion.Offset.Y);

                if (constrainedHeight < 0)
                {
                    constrainedHeight = 0;
                }
            }

            clearRegion = new Rect2D(clearRegion.Offset, new Extent2D(constrainedWidth, constrainedHeight));

            commandBuffer.ClearAttachments(
                    new ClearAttachment
                    {
                        AspectMask = ImageAspectFlags.Color,
                        ClearValue = new ClearColorValue(0f, 0f, 1f, 1f),
                        ColorAttachment = 0
                    },
                    new ClearRect
                    {
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                        Rect = clearRegion
                    });
        }
    }
}
