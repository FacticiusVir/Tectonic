using GlmSharp;
using SharpVk;

namespace Tectonic
{
    public class ClearStage
        : RenderStage
    {
        public vec4? ClearColour
        {
            get;
            set;
        }

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

            vec4 clearColour = this.ClearColour ?? new vec4(0, 0, 0, 1);

            commandBuffer.ClearAttachments(
                    new[]
                    {
                        new ClearAttachment
                        {
                            AspectMask = ImageAspectFlags.Color,
                            ClearValue = new ClearColorValue(clearColour.x, clearColour.y, clearColour.z, clearColour.w),
                            ColorAttachment = 0
                        },
                        new ClearAttachment
                        {
                            AspectMask = ImageAspectFlags.Depth,
                            ClearValue = new ClearDepthStencilValue(1f, 0),
                            ColorAttachment = 1
                        }
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
