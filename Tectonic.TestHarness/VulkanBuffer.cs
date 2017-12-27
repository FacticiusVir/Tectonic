using SharpVk;

namespace Tectonic
{
    public class VulkanBuffer
    {
        private readonly VulkanBufferManager manager;
        private readonly Buffer buffer;

        public VulkanBuffer(VulkanBufferManager manager, Buffer buffer)
        {
            this.manager = manager;
            this.buffer = buffer;
        }

        public Buffer Buffer => this.buffer;
        
        public void Update<T>(T data, int offset = 0)
            where T : struct
        {
            this.manager.UpdateBuffer(this.buffer, data, offset);
        }

        public void Update<T>(T[] data, int offset = 0)
            where T : struct
        {
            this.manager.UpdateBuffer(this.buffer, data, offset);
        }
    }
}
