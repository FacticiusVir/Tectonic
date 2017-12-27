using SharpVk;

namespace Tectonic
{
    public class VulkanImage
    {
        private readonly VulkanBufferManager manager;
        private readonly Image image;
        private readonly Format format;

        public VulkanImage(VulkanBufferManager manager, Image image, Format format)
        {
            this.manager = manager;
            this.image = image;
            this.format = format;
        }

        public Image Image => this.image;

        public Format Format => this.format;

        public void TransitionImageLayout(ImageLayout oldLayout, ImageLayout newLayout)
        {
            this.manager.TransitionImageLayout(this.image, this.format, oldLayout, newLayout);
        }
    }
}
