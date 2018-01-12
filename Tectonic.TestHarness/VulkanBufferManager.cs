using SharpVk;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tectonic
{
    public class VulkanBufferManager
    {
        private readonly PhysicalDevice physicalDevice;
        private readonly Device device;
        private readonly Queue transferQueue;
        private readonly CommandPool transientCommandPool;

        private Buffer stagingBuffer;
        private DeviceMemory stagingBufferMemory;
        private uint stagingBufferSize;

        public VulkanBufferManager(PhysicalDevice physicalDevice, Device device, Queue transferQueue, uint transferQueueFamily)
        {
            this.physicalDevice = physicalDevice;
            this.device = device;
            this.transferQueue = transferQueue;

            this.transientCommandPool = device.CreateCommandPool(transferQueueFamily, CommandPoolCreateFlags.Transient);
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

        public VulkanBuffer CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
        {
            this.CreateBuffer(size, usage, properties, out var buffer, out var _, out DeviceSize _);

            return new VulkanBuffer(this, buffer, size);
        }

        public VulkanImage CreateImage(uint width, uint height, Format format, ImageTiling imageTiling, ImageUsageFlags usage, MemoryPropertyFlags properties)
        {
            this.CreateImage(width, height, format, imageTiling, usage, properties, out var image, out var imageMemory, out DeviceSize offset, out DeviceSize size);

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
            var transferBuffers = this.BeginSingleTimeCommand();

            transferBuffers[0].CopyBuffer(sourceBuffer, destinationBuffer, new[] { new BufferCopy { SourceOffset = offset, DestinationOffset = offset, Size = size } });

            this.EndSingleTimeCommand(transferBuffers);
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
            var transferBuffers = this.BeginSingleTimeCommand();

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

            this.EndSingleTimeCommand(transferBuffers);
        }

        internal void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
        {
            var commandBuffer = this.BeginSingleTimeCommand();

            var barrier = new ImageMemoryBarrier
            {
                OldLayout = oldLayout,
                NewLayout = newLayout,
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

            if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.TransferSourceOptimal)
            {
                barrier.SourceAccessMask = AccessFlags.HostWrite;
                barrier.DestinationAccessMask = AccessFlags.TransferRead;
            }
            else if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.TransferDestinationOptimal)
            {
                barrier.SourceAccessMask = AccessFlags.HostWrite;
                barrier.DestinationAccessMask = AccessFlags.TransferWrite;
            }
            else if (oldLayout == ImageLayout.TransferDestinationOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SourceAccessMask = AccessFlags.TransferWrite;
                barrier.DestinationAccessMask = AccessFlags.ShaderRead;
            }
            else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
            {
                barrier.SourceAccessMask = AccessFlags.None;
                barrier.DestinationAccessMask = AccessFlags.DepthStencilAttachmentRead | AccessFlags.DepthStencilAttachmentWrite;
            }
            else
            {
                throw new System.Exception("Unsupported layout transition");
            }

            commandBuffer[0].PipelineBarrier(PipelineStageFlags.TopOfPipe,
                                                PipelineStageFlags.TopOfPipe,
                                                null,
                                                null,
                                                new[] { barrier });

            this.EndSingleTimeCommand(commandBuffer);
        }

        private CommandBuffer[] BeginSingleTimeCommand()
        {
            var result = device.AllocateCommandBuffers(this.transientCommandPool, CommandBufferLevel.Primary, 1);

            result[0].Begin(CommandBufferUsageFlags.OneTimeSubmit);

            return result;
        }

        private void EndSingleTimeCommand(CommandBuffer[] transferBuffers)
        {
            transferBuffers[0].End();

            this.transferQueue.Submit(new[] { new SubmitInfo { CommandBuffers = transferBuffers } }, null);
            this.transferQueue.WaitIdle();

            this.transientCommandPool.FreeCommandBuffers(transferBuffers);
        }

        private void CreateImage(uint width, uint height, Format format, ImageTiling imageTiling, ImageUsageFlags usage, MemoryPropertyFlags properties, out Image image, out DeviceMemory imageMemory, out DeviceSize memoryOffset, out DeviceSize memorySize)
        {
            image = this.device.CreateImage(ImageType.Image2d, format, new Extent3D(width, height, 1), 1, 1, SampleCountFlags.SampleCount1, imageTiling, usage, SharingMode.Exclusive, null, ImageLayout.Preinitialized);

            var memoryRequirements = image.GetMemoryRequirements();

            memorySize = memoryRequirements.Size;

            imageMemory = this.device.AllocateMemory(memorySize, this.FindMemoryType(memoryRequirements.MemoryTypeBits, properties));

            memoryOffset = 0;

            image.BindMemory(imageMemory, memoryOffset);
        }
    }
}
