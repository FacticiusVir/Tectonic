using SharpVk;
using System;

namespace Tectonic
{
    public class VulkanImage
        : IDisposable
    {
        private readonly VulkanBufferManager manager;
        private readonly Image image;
        private readonly DeviceMemory memory;
        private readonly DeviceSize offset;
        private readonly DeviceSize size;
        private readonly Format format;

        public VulkanImage(VulkanBufferManager manager, Image image, DeviceMemory memory, DeviceSize offset, DeviceSize size, Format format)
        {
            this.manager = manager;
            this.image = image;
            this.memory = memory;
            this.offset = offset;
            this.size = size;
            this.format = format;
        }

        public Image Image => this.image;

        public Format Format => this.format;

        public IntPtr Map()
        {
            return this.memory.Map(this.offset, this.size);
        }

        public void Unmap()
        {
            this.memory.Unmap();
        }

        public void TransitionImageLayout(ImageLayout oldLayout, ImageLayout newLayout)
        {
            this.manager.TransitionImageLayout(this.image, this.format, oldLayout, newLayout);
        }

        public void Copy(VulkanImage destination, uint width, uint height)
        {
            this.manager.Copy(this.image, destination.image, width, height);
        }

        public void Dispose()
        {
            this.image.Dispose();
            this.memory.Free();
        }
    }
}
