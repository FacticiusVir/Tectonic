using SharpVk;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Tectonic
{
    public class VulkanBufferManager
    {
        private readonly PhysicalDevice physicalDevice;
        private readonly Device device;
        private readonly Queue barrierQueue;
        private readonly Queue transferQueue;
        private readonly VulkanDeviceService.QueueFamilyIndices queueIndices;
        private readonly CommandPool transferCommandPool;
        private readonly CommandPool barrierCommandPool;
        private Buffer stagingBuffer;
        private DeviceMemory stagingBufferMemory;
        private uint stagingBufferSize;

        internal VulkanBufferManager(PhysicalDevice physicalDevice, Device device, Queue graphicsQueue, Queue transferQueue, VulkanDeviceService.QueueFamilyIndices queueIndices)
        {
            this.physicalDevice = physicalDevice;
            this.device = device;
            this.barrierQueue = graphicsQueue;
            this.transferQueue = transferQueue;

            this.queueIndices = queueIndices;

            this.transferCommandPool = device.CreateCommandPool(this.queueIndices.TransferFamily.Value, CommandPoolCreateFlags.Transient);
            this.barrierCommandPool = device.CreateCommandPool(this.queueIndices.GraphicsFamily.Value, CommandPoolCreateFlags.Transient);
        }

        private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags flags)
        {
            var memoryProperties = this.physicalDevice.GetMemoryProperties();

            for (int i = 0; i < memoryProperties.MemoryTypes.Length; i++)
            {
                if ((typeFilter & (1u << i)) > 0
                        && memoryProperties.MemoryTypes[i].PropertyFlags.HasFlag(flags))
                {
                    return (uint)i;
                }
            }

            throw new System.Exception("No compatible memory type.");
        }

        public VulkanBuffer CreateBuffer<T>(int count, BufferUsageFlags usage, MemoryPropertyFlags properties)
        {
            return this.CreateBuffer((uint)(Marshal.SizeOf<T>() * count), usage, properties);
        }

        public VulkanBuffer CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
        {
            this.CreateBuffer(size, usage, properties, out var buffer, out var _, out DeviceSize _);

            return new VulkanBuffer(this, buffer, size);
        }

        public VulkanImage CreateImage(uint width, uint height, Format format, ImageTiling imageTiling, ImageUsageFlags usage, MemoryPropertyFlags properties, bool isPreinitialized)
        {
            this.CreateImage(width, height, format, imageTiling, usage, properties, isPreinitialized, out var image, out var imageMemory, out DeviceSize offset, out DeviceSize size);

            return new VulkanImage(this, image, imageMemory, offset, size, format);
        }

        private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory bufferMemory, out DeviceSize memoryOffset)
        {
            buffer = device.CreateBuffer(size, usage, SharingMode.Exclusive, null);

            var memRequirements = buffer.GetMemoryRequirements();

            bufferMemory = device.AllocateMemory(memRequirements.Size, FindMemoryType(memRequirements.MemoryTypeBits, properties));

            memoryOffset = 0;

            buffer.BindMemory(bufferMemory, memoryOffset);
        }

        internal void Copy(Buffer sourceBuffer, Buffer destinationBuffer, ulong offset, ulong size)
        {
            var transferBuffers = this.BeginSingleTimeCommand(this.transferCommandPool);

            transferBuffers[0].CopyBuffer(sourceBuffer, destinationBuffer, new[] { new BufferCopy { SourceOffset = offset, DestinationOffset = offset, Size = size } });

            this.EndSingleTimeCommand(transferBuffers, this.transferCommandPool, this.transferQueue);
        }

        internal void UpdateBuffer<T>(Buffer buffer, T data, int offset = 0)
            where T : struct
        {
            uint datumSize = (uint)Marshal.SizeOf<T>();
            uint dataOffset = (uint)(offset * datumSize);

            this.CheckStagingBufferSize(datumSize, dataOffset);

            System.IntPtr memoryBuffer = this.stagingBufferMemory.Map(dataOffset, datumSize);

            Marshal.StructureToPtr(data, memoryBuffer, false);

            this.stagingBufferMemory.Unmap();

            this.Copy(this.stagingBuffer, buffer, dataOffset, datumSize);
        }

        internal void UpdateBuffer<T>(Buffer buffer, T[] data, DeviceSize maxSize, int offset = 0)
            where T : struct
        {
            uint datumSize = (uint)Marshal.SizeOf<T>();
            uint dataSize = (uint)(datumSize * data.Length);
            uint dataOffset = (uint)(offset * datumSize);

            Debug.Assert(dataOffset + dataSize <= maxSize);

            this.CheckStagingBufferSize(dataSize, dataOffset);

            System.IntPtr memoryBuffer = this.stagingBufferMemory.Map(dataOffset, dataSize);

            for (int index = 0; index < data.Length; index++)
            {
                Marshal.StructureToPtr(data[index], memoryBuffer, false);

                memoryBuffer += Marshal.SizeOf<T>();
            }

            this.stagingBufferMemory.Unmap();

            this.Copy(this.stagingBuffer, buffer, dataOffset, dataSize);
        }


        internal void CheckStagingBufferSize(uint dataSize, uint dataOffset)
        {
            uint memRequirement = dataOffset + dataSize;

            if (memRequirement > this.stagingBufferSize)
            {
                if (stagingBuffer != null)
                {
                    this.stagingBuffer.Destroy();
                    this.stagingBufferMemory.Free();
                }

                this.CreateBuffer(memRequirement, BufferUsageFlags.TransferSource, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out this.stagingBuffer, out this.stagingBufferMemory, out DeviceSize _);
                this.stagingBufferSize = memRequirement;
            }
        }

        internal void Copy(Image sourceImage, Image destinationImage, uint width, uint height)
        {
            var transferBuffers = this.BeginSingleTimeCommand(this.transferCommandPool);

            ImageSubresourceLayers subresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.Color,
                BaseArrayLayer = 0,
                LayerCount = 1,
                MipLevel = 0
            };

            ImageCopy region = new ImageCopy
            {
                DestinationSubresource = subresource,
                SourceSubresource = subresource,
                SourceOffset = new Offset3D(),
                DestinationOffset = new Offset3D(),
                Extent = new Extent3D
                {
                    Width = width,
                    Height = height,
                    Depth = 1
                }
            };

            transferBuffers[0].CopyImage(sourceImage, ImageLayout.TransferSourceOptimal, destinationImage, ImageLayout.TransferDestinationOptimal, region);

            this.EndSingleTimeCommand(transferBuffers, this.transferCommandPool, this.transferQueue);
        }

        internal void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
        {
            PipelineStageFlags sourceStage = PipelineStageFlags.TopOfPipe;
            PipelineStageFlags destinationStage = PipelineStageFlags.TopOfPipe;

            AccessFlags MapAccessMask(ImageLayout layout, ref PipelineStageFlags stage)
            {
                switch (layout)
                {
                    case ImageLayout.Undefined:
                        return AccessFlags.None;
                    case ImageLayout.Preinitialized:
                        stage = PipelineStageFlags.Host;
                        return AccessFlags.HostWrite;
                    case ImageLayout.TransferSourceOptimal:
                        stage = PipelineStageFlags.Transfer;
                        return AccessFlags.TransferRead;
                    case ImageLayout.TransferDestinationOptimal:
                        stage = PipelineStageFlags.Transfer;
                        return AccessFlags.TransferWrite;
                    case ImageLayout.ShaderReadOnlyOptimal:
                        stage = PipelineStageFlags.VertexShader | PipelineStageFlags.FragmentShader;
                        return AccessFlags.ShaderRead;
                    case ImageLayout.DepthStencilAttachmentOptimal:
                        stage = PipelineStageFlags.EarlyFragmentTests | PipelineStageFlags.LateFragmentTests;
                        return AccessFlags.DepthStencilAttachmentRead | AccessFlags.DepthStencilAttachmentWrite;
                }

                throw new System.Exception($"Unsupported layout transition '{layout}'");
            }

            var commandBuffer = this.BeginSingleTimeCommand(this.barrierCommandPool);

            var barrier = new ImageMemoryBarrier
            {
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SourceAccessMask = MapAccessMask(oldLayout, ref sourceStage),
                DestinationAccessMask = MapAccessMask(newLayout, ref destinationStage),
                SourceQueueFamilyIndex = Constants.QueueFamilyIgnored,
                DestinationQueueFamilyIndex = Constants.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = newLayout == ImageLayout.DepthStencilAttachmentOptimal
                                    ? ImageAspectFlags.Depth
                                    : ImageAspectFlags.Color,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };
            
            commandBuffer[0].PipelineBarrier(sourceStage,
                                                destinationStage,
                                                null,
                                                null,
                                                barrier);

            this.EndSingleTimeCommand(commandBuffer, this.barrierCommandPool, this.barrierQueue);
        }

        private CommandBuffer[] BeginSingleTimeCommand(CommandPool pool)
        {
            var result = device.AllocateCommandBuffers(pool, CommandBufferLevel.Primary, 1);

            result[0].Begin(CommandBufferUsageFlags.OneTimeSubmit);

            return result;
        }

        private void EndSingleTimeCommand(CommandBuffer[] buffers, CommandPool pool, Queue queue)
        {
            buffers[0].End();

            queue.Submit(new SubmitInfo { CommandBuffers = buffers }, null);
            queue.WaitIdle();

            pool.FreeCommandBuffers(buffers);
        }

        private void CreateImage(uint width, uint height, Format format, ImageTiling imageTiling, ImageUsageFlags usage, MemoryPropertyFlags properties, bool isPreinitialized, out Image image, out DeviceMemory imageMemory, out DeviceSize memoryOffset, out DeviceSize memorySize)
        {
            image = this.device.CreateImage(ImageType.Image2d, format, new Extent3D(width, height, 1), 1, 1, SampleCountFlags.SampleCount1, imageTiling, usage, this.queueIndices.Indices.Count() == 1 ? SharingMode.Exclusive : SharingMode.Concurrent, this.queueIndices.Indices.ToArray(), isPreinitialized ? ImageLayout.Preinitialized : ImageLayout.Undefined);

            var memoryRequirements = image.GetMemoryRequirements();

            memorySize = memoryRequirements.Size;

            imageMemory = this.device.AllocateMemory(memorySize, this.FindMemoryType(memoryRequirements.MemoryTypeBits, properties));

            memoryOffset = 0;

            image.BindMemory(imageMemory, memoryOffset);
        }
    }
}
