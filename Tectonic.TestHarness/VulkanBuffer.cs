using SharpVk;

namespace Tectonic
{
    public class VulkanBuffer
    {
        private readonly VulkanBufferManager manager;
        private readonly Buffer buffer;
        private readonly DeviceSize size;

        public VulkanBuffer(VulkanBufferManager manager, Buffer buffer, DeviceSize size)
        {
            this.manager = manager;
            this.buffer = buffer;
            this.size = size;
        }

        public Buffer Buffer => this.buffer;

        public DeviceSize Size => this.size;
        
        public void Update<T>(T data, int offset = 0)
            where T : struct
        {
            this.manager.UpdateBuffer(this.buffer, data, offset);
        }

        public void Update<T>(T[] data, int offset = 0)
            where T : struct
        {
            this.manager.UpdateBuffer(this.buffer, data, this.size, offset);
        }
    }
}
