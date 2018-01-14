using GlmSharp;
using SharpVk;
using SharpVk.Glfw;
using SharpVk.Khronos;
using SharpVk.Multivendor;
using System;
using System.Collections.Generic;
using System.Linq;

using Size = SharpVk.HostSize;

namespace Tectonic
{
    public class VulkanDeviceService
        : GameService, IUpdatable
    {
        // A managed reference is held to prevent the delegate from being
        // garbage-collected while still in use by the unmanaged API.
        private readonly DebugReportCallbackDelegate debugReportDelegate;
        private readonly IUpdateLoopService updateLoop;
        private readonly GlfwService glfwService;
        private readonly IServiceProvider provider;
        private Instance instance;
        private DebugReportCallback reportCallback;
        private Surface surface;
        private PhysicalDevice physicalDevice;
        private Device device;
        private Queue graphicsQueue;
        private Queue presentQueue;
        private Queue transferQueue;
        private Swapchain swapChain;
        private RenderPass renderPass;
        private Image[] swapChainImages;
        private ImageView[] swapChainImageViews;
        private Framebuffer[] swapChainFrameBuffers;
        private CommandPool commandPool;
        private CommandBuffer[] beginCommandBuffers;
        private CommandBuffer[,] renderStageCommandBuffers;
        private CommandBuffer[] endCommandBuffers;
        private VulkanImage[] depthImages;
        private ImageView[] depthImageViews;

        private Semaphore imageAvailableSemaphore;
        private Semaphore renderBeginSemaphore;
        private List<Semaphore> renderStageSemaphores = new List<Semaphore>();
        private Semaphore renderEndSemaphore;


        private Format swapChainFormat;
        private Extent2D swapChainExtent;

        private bool isCommandBufferStale;

        private readonly List<RenderStage> renderStages = new List<RenderStage>();
        private VulkanBufferManager bufferManager;

        public VulkanDeviceService(IUpdateLoopService updateLoop, GlfwService glfwService, IServiceProvider provider)
        {
            this.debugReportDelegate = this.DebugReport;
            this.updateLoop = updateLoop;
            this.glfwService = glfwService;
            this.provider = provider;
        }

        public override void Initialise(Game game)
        {
            this.CreateInstance();
        }

        public override void Start()
        {
            this.CreateSurface();
            this.PickPhysicalDevice();
            this.CreateLogicalDevice();

            var queueFamilies = FindQueueFamilies(this.physicalDevice);
            this.bufferManager = new VulkanBufferManager(this.physicalDevice, this.device, this.graphicsQueue, this.transferQueue, queueFamilies);

            this.CreateSwapChain();
            this.CreateCommandPool();
            this.CreateDepthResources();
            this.CreateRenderPass();
            this.CreateFrameBuffers();
            this.CreateSemaphores();

            foreach (var stage in this.renderStages)
            {
                stage.Initialise(this.device, this.bufferManager);
            }

            this.isCommandBufferStale = true;

            this.updateLoop.Register(this, UpdateStage.Render);
        }

        public void Update()
        {
            if (this.glfwService.IsResized)
            {
                this.RecreateSwapChain();
            }

            if (this.isCommandBufferStale)
            {
                this.CreateCommandBuffers();

                this.isCommandBufferStale = false;
            }

            foreach (var stage in this.renderStages)
            {
                stage.Update();
            }

            uint nextImage = this.swapChain.AcquireNextImage(uint.MaxValue, this.imageAvailableSemaphore, null);

            this.graphicsQueue.Submit(
                new SubmitInfo
                {
                    CommandBuffers = new[] { this.beginCommandBuffers[nextImage] },
                    SignalSemaphores = new[] { this.renderBeginSemaphore },
                    WaitDestinationStageMask = new[] { PipelineStageFlags.ColorAttachmentOutput },
                    WaitSemaphores = new[] { this.imageAvailableSemaphore }
                }, null);

            var previousSemaphore = this.renderBeginSemaphore;

            for (int index = 0; index < this.renderStages.Count; index++)
            {
                this.graphicsQueue.Submit(
                        new SubmitInfo
                        {
                            CommandBuffers = new[] { this.renderStageCommandBuffers[nextImage, index] },
                            SignalSemaphores = new[] { this.renderStageSemaphores[index] },
                            WaitDestinationStageMask = new[] { PipelineStageFlags.ColorAttachmentOutput },
                            WaitSemaphores = new[] { previousSemaphore }
                        }, null);

                previousSemaphore = this.renderStageSemaphores[index];
            }

            this.graphicsQueue.Submit(
                new SubmitInfo
                {
                    CommandBuffers = new[] { this.endCommandBuffers[nextImage] },
                    SignalSemaphores = new[] { this.renderEndSemaphore },
                    WaitDestinationStageMask = new[] { PipelineStageFlags.ColorAttachmentOutput },
                    WaitSemaphores = new[] { previousSemaphore }
                }, null);

            this.presentQueue.WaitIdle();

            this.presentQueue.Present(this.renderEndSemaphore, this.swapChain, nextImage, new Result[1]);
        }

        public override void Stop()
        {
            this.updateLoop.Deregister(this);

            this.swapChain.Destroy();
            this.swapChain = null;

            this.surface.Destroy();
            this.surface = null;

            this.reportCallback.Destroy();
            this.reportCallback = null;

            this.instance.Destroy();
            this.instance = null;
        }

        public T CreateStage<T>(params object[] parameters)
            where T : RenderStage
        {
            var result = this.provider.CreateInstance<T>(parameters);

            if (this.device != null)
            {
                result.Initialise(device, this.bufferManager);
            }

            this.renderStages.Add(result);

            this.isCommandBufferStale = true;

            return result;
        }

        private void RecreateSwapChain()
        {
            this.device.WaitIdle();

            var oldSwapChain = this.swapChain;

            this.CreateSwapChain();
            this.CreateDepthResources();
            this.CreateRenderPass();
            this.CreateFrameBuffers();

            oldSwapChain.Dispose();

            this.isCommandBufferStale = true;
        }

        private void CreateInstance()
        {
            var enabledLayers = new List<string>();

            if (Instance.EnumerateLayerProperties().Any(x => x.LayerName == "VK_LAYER_LUNARG_standard_validation"))
            {
                enabledLayers.Add("VK_LAYER_LUNARG_standard_validation");
            }

            var glfwExtensions = Glfw3.GetRequiredInstanceExtensions();

            this.instance = Instance.Create(enabledLayers.ToArray(), glfwExtensions.Concat(new[] { ExtExtensions.DebugReport }).ToArray(), InstanceCreateFlags.None,
                new ApplicationInfo
                {
                    ApplicationName = "Tectonic",
                    ApplicationVersion = new SharpVk.Version(1, 0, 0),
                    EngineName = "Tectonic",
                    ApiVersion = new SharpVk.Version(1, 0, 0)
                });

            this.reportCallback = this.instance.CreateDebugReportCallback(this.debugReportDelegate, DebugReportFlags.Error | DebugReportFlags.Warning);
        }

        private void CreateSurface()
        {
            this.surface = this.instance.CreateGlfw3Surface(this.glfwService.WindowHandle);
        }

        private void PickPhysicalDevice()
        {
            var availableDevices = this.instance.EnumeratePhysicalDevices();

            this.physicalDevice = availableDevices.First(IsSuitableDevice);
        }

        private void CreateLogicalDevice()
        {
            QueueFamilyIndices queueFamilies = FindQueueFamilies(this.physicalDevice);

            this.device = physicalDevice.CreateDevice(queueFamilies.Indices
                                                        .Select(index => new DeviceQueueCreateInfo
                                                        {
                                                            QueueFamilyIndex = index,
                                                            QueuePriorities = new[] { 1f }
                                                        }).ToArray(),
                                                        null,
                                                        KhrExtensions.Swapchain);

            this.graphicsQueue = this.device.GetQueue(queueFamilies.GraphicsFamily.Value, 0);
            this.presentQueue = this.device.GetQueue(queueFamilies.PresentFamily.Value, 0);
            this.transferQueue = this.device.GetQueue(queueFamilies.TransferFamily.Value, 0);
        }

        private void CreateRenderPass()
        {
            this.renderPass = this.device.CreateRenderPass(
                new[]
                {
                    new AttachmentDescription
                    {
                        Format = this.swapChainFormat,
                        Samples = SampleCountFlags.SampleCount1,
                        LoadOp = AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                        StencilLoadOp = AttachmentLoadOp.DontCare,
                        StencilStoreOp = AttachmentStoreOp.DontCare,
                        InitialLayout = ImageLayout.ColorAttachmentOptimal,
                        FinalLayout = ImageLayout.ColorAttachmentOptimal
                    },
                    new AttachmentDescription
                    {
                        Format = this.depthImages[0].Format,
                        Samples = SampleCountFlags.SampleCount1,
                        LoadOp = AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                        StencilLoadOp = AttachmentLoadOp.DontCare,
                        StencilStoreOp = AttachmentStoreOp.DontCare,
                        InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
                        FinalLayout = ImageLayout.DepthStencilAttachmentOptimal

                    }
                },
                new[]
                {
                    new SubpassDescription
                    {
                        DepthStencilAttachment = new AttachmentReference
                        {
                            Attachment = 1,
                            Layout = ImageLayout.DepthStencilAttachmentOptimal
                        },
                        PipelineBindPoint = PipelineBindPoint.Graphics,
                        ColorAttachments = new []
                        {
                            new AttachmentReference
                            {
                                Attachment = 0,
                                Layout = ImageLayout.ColorAttachmentOptimal
                            }
                        }
                    }
                },
                new[]
                {
                    new SubpassDependency
                    {
                        SourceSubpass = Constants.SubpassExternal,
                        DestinationSubpass = 0,
                        SourceStageMask = PipelineStageFlags.BottomOfPipe,
                        SourceAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite,
                        DestinationStageMask = PipelineStageFlags.ColorAttachmentOutput,
                        DestinationAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite
                    },
                    new SubpassDependency
                    {
                        SourceSubpass = 0,
                        DestinationSubpass = Constants.SubpassExternal,
                        SourceStageMask = PipelineStageFlags.ColorAttachmentOutput,
                        SourceAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite,
                        DestinationStageMask = PipelineStageFlags.TopOfPipe,
                        DestinationAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite
                    }
                });
        }

        private void CreateSwapChain()
        {
            SwapChainSupportDetails swapChainSupport = this.QuerySwapChainSupport(this.physicalDevice);

            uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
            {
                imageCount = swapChainSupport.Capabilities.MaxImageCount;
            }

            SurfaceFormat surfaceFormat = this.ChooseSwapSurfaceFormat(swapChainSupport.Formats);

            QueueFamilyIndices queueFamilies = this.FindQueueFamilies(this.physicalDevice);

            var indices = queueFamilies.Indices.ToArray();

            Extent2D extent = this.ChooseSwapExtent(swapChainSupport.Capabilities);

            this.swapChain = device.CreateSwapchain(surface,
                                                    imageCount,
                                                    surfaceFormat.Format,
                                                    surfaceFormat.ColorSpace,
                                                    extent,
                                                    1,
                                                    ImageUsageFlags.ColorAttachment | ImageUsageFlags.TransferDestination,
                                                    indices.Length == 1
                                                        ? SharingMode.Exclusive
                                                        : SharingMode.Concurrent,
                                                    indices,
                                                    swapChainSupport.Capabilities.CurrentTransform,
                                                    CompositeAlphaFlags.Opaque,
                                                    this.ChooseSwapPresentMode(swapChainSupport.PresentModes),
                                                    true,
                                                    this.swapChain);

            this.swapChainFormat = surfaceFormat.Format;
            this.swapChainExtent = extent;

            this.swapChainImages = this.swapChain.GetImages();

            this.swapChainImageViews = swapChainImages.Select(image => this.CreateImageView(image, this.swapChainFormat, ImageAspectFlags.Color)).ToArray();
        }

        private void CreateFrameBuffers()
        {
            this.swapChainFrameBuffers = this.swapChainImageViews.Select((imageView, index) => this.device.CreateFramebuffer(
                this.renderPass,
                new[] { imageView, this.depthImageViews[index] },
                this.swapChainExtent.Width,
                this.swapChainExtent.Height,
                1)).ToArray();
        }

        private void CreateCommandPool()
        {
            QueueFamilyIndices queueFamilies = FindQueueFamilies(this.physicalDevice);

            this.commandPool = device.CreateCommandPool(queueFamilies.GraphicsFamily.Value);
        }

        private void CreateCommandBuffers()
        {
            this.beginCommandBuffers = this.device.AllocateCommandBuffers(commandPool, CommandBufferLevel.Primary, (uint)this.swapChainFrameBuffers.Length);

            this.endCommandBuffers = this.device.AllocateCommandBuffers(commandPool, CommandBufferLevel.Primary, (uint)this.swapChainFrameBuffers.Length);

            var commandBuffers = this.device.AllocateCommandBuffers(commandPool, CommandBufferLevel.Primary, (uint)(this.swapChainFrameBuffers.Length * this.renderStages.Count));

            this.renderStageCommandBuffers = new CommandBuffer[this.swapChainFrameBuffers.Length, this.renderStages.Count];

            while (this.renderStageSemaphores.Count < this.renderStages.Count)
            {
                this.renderStageSemaphores.Add(this.device.CreateSemaphore());
            }

            var imageColorRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.Color,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            };

            var imageDepthRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.Depth,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            };

            for (int bufferIndex = 0; bufferIndex < this.swapChainFrameBuffers.Length; bufferIndex++)
            {
                var beginBuffer = this.beginCommandBuffers[bufferIndex];

                beginBuffer.Begin(CommandBufferUsageFlags.SimultaneousUse);

                beginBuffer.PipelineBarrier(PipelineStageFlags.TopOfPipe,
                                            PipelineStageFlags.Transfer,
                                            null,
                                            null,
                                            new[]
                                            {
                                                new ImageMemoryBarrier
                                                {
                                                    OldLayout = ImageLayout.Undefined,
                                                    NewLayout = ImageLayout.TransferDestinationOptimal,
                                                    SourceQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                    DestinationQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                    SourceAccessMask = AccessFlags.None,
                                                    DestinationAccessMask = AccessFlags.TransferWrite,
                                                    Image = this.swapChainImages[bufferIndex],
                                                    SubresourceRange = imageColorRange
                                                },
                                                new ImageMemoryBarrier
                                                {
                                                    OldLayout = ImageLayout.Undefined,
                                                    NewLayout = ImageLayout.TransferDestinationOptimal,
                                                    SourceQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                    DestinationQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                    SourceAccessMask = AccessFlags.None,
                                                    DestinationAccessMask = AccessFlags.TransferWrite,
                                                    Image = this.depthImages[bufferIndex].Image,
                                                    SubresourceRange = imageDepthRange
                                                }
                                            });

                beginBuffer.ClearColorImage(this.swapChainImages[bufferIndex], ImageLayout.TransferDestinationOptimal, (0f, 0f, 0f, 1f), imageColorRange);
                beginBuffer.ClearDepthStencilImage(this.depthImages[bufferIndex].Image, ImageLayout.TransferDestinationOptimal, new ClearDepthStencilValue(1f, 0), imageDepthRange);

                beginBuffer.PipelineBarrier(PipelineStageFlags.Transfer,
                                            PipelineStageFlags.ColorAttachmentOutput,
                                            null,
                                            null,
                                            new ImageMemoryBarrier
                                            {
                                                OldLayout = ImageLayout.TransferDestinationOptimal,
                                                NewLayout = ImageLayout.ColorAttachmentOptimal,
                                                SourceQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                DestinationQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                SourceAccessMask = AccessFlags.TransferWrite,
                                                DestinationAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite,
                                                Image = this.swapChainImages[bufferIndex],
                                                SubresourceRange = imageColorRange
                                            });

                beginBuffer.PipelineBarrier(PipelineStageFlags.Transfer,
                                            PipelineStageFlags.EarlyFragmentTests,
                                            null,
                                            null,
                                            new ImageMemoryBarrier
                                            {
                                                OldLayout = ImageLayout.TransferDestinationOptimal,
                                                NewLayout = ImageLayout.DepthStencilAttachmentOptimal,
                                                SourceQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                DestinationQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                SourceAccessMask = AccessFlags.TransferWrite,
                                                DestinationAccessMask = AccessFlags.DepthStencilAttachmentRead | AccessFlags.DepthStencilAttachmentWrite,
                                                Image = this.depthImages[bufferIndex].Image,
                                                SubresourceRange = imageDepthRange
                                            });

                beginBuffer.End();

                var endBuffer = this.endCommandBuffers[bufferIndex];

                endBuffer.Begin(CommandBufferUsageFlags.SimultaneousUse);

                endBuffer.PipelineBarrier(PipelineStageFlags.ColorAttachmentOutput,
                                            PipelineStageFlags.TopOfPipe,
                                            null,
                                            null,
                                            new ImageMemoryBarrier
                                            {
                                                OldLayout = ImageLayout.ColorAttachmentOptimal,
                                                NewLayout = ImageLayout.PresentSource,
                                                SourceQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                DestinationQueueFamilyIndex = Constants.QueueFamilyIgnored,
                                                SourceAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite,
                                                DestinationAccessMask = AccessFlags.MemoryRead,
                                                Image = this.swapChainImages[bufferIndex],
                                                SubresourceRange = imageColorRange
                                            });

                endBuffer.End();

                for (int stageIndex = 0; stageIndex < this.renderStages.Count; stageIndex++)
                {
                    var commandBuffer = commandBuffers[bufferIndex * this.renderStages.Count + stageIndex];
                    this.renderStageCommandBuffers[bufferIndex, stageIndex] = commandBuffer;
                    var frameBuffer = this.swapChainFrameBuffers[bufferIndex];

                    commandBuffer.Begin(CommandBufferUsageFlags.SimultaneousUse);

                    commandBuffer.BeginRenderPass(renderPass, frameBuffer, new Rect2D(this.swapChainExtent), new ClearValue[2], SubpassContents.Inline);

                    foreach (var renderStage in this.renderStages)
                    {
                        renderStage.Bind(this.device, this.renderPass, commandBuffer, this.swapChainExtent);
                    }

                    commandBuffer.EndRenderPass();

                    commandBuffer.End();
                }
            }
        }

        private void CreateDepthResources()
        {
            Format depthFormat = this.FindDepthFormat();

            this.depthImages = new VulkanImage[this.swapChainImages.Length];

            for (int index = 0; index < this.swapChainImages.Length; index++)
            {
                this.depthImages[index] = this.bufferManager.CreateImage(this.swapChainExtent.Width,
                                                                            this.swapChainExtent.Height,
                                                                            depthFormat,
                                                                            ImageTiling.Optimal,
                                                                            ImageUsageFlags.DepthStencilAttachment | ImageUsageFlags.TransferDestination,
                                                                            MemoryPropertyFlags.DeviceLocal,
                                                                            false);

                this.depthImages[index].TransitionImageLayout(ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal);
            }

            this.depthImageViews = this.depthImages.Select(image => this.CreateImageView(image.Image, depthFormat, ImageAspectFlags.Depth)).ToArray();
        }

        private void CreateSemaphores()
        {
            this.imageAvailableSemaphore = device.CreateSemaphore();
            this.renderBeginSemaphore = device.CreateSemaphore();
            this.renderEndSemaphore = device.CreateSemaphore();
        }

        private Format FindDepthFormat()
        {
            return this.FindSupportedFormat(new[] { Format.D32SFloat, Format.D32SFloatS8UInt, Format.D24UNormS8UInt }, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachment);
        }

        private static bool HasStencilComponent(Format format)
        {
            return format == Format.D32SFloatS8UInt || format == Format.D24UNormS8UInt;
        }

        private Format FindSupportedFormat(IEnumerable<Format> candidates, ImageTiling tiling, FormatFeatureFlags features)
        {
            foreach (var format in candidates)
            {
                var props = this.physicalDevice.GetFormatProperties(format);

                if (tiling == ImageTiling.Linear && props.LinearTilingFeatures.HasFlag(features))
                {
                    return format;
                }
                else if (tiling == ImageTiling.Optimal && props.OptimalTilingFeatures.HasFlag(features))
                {
                    return format;
                }
            }

            throw new Exception("failed to find supported format!");
        }

        private Bool32 DebugReport(DebugReportFlags flags, DebugReportObjectType objectType, ulong @object, Size location, int messageCode, string layerPrefix, string message, IntPtr userData)
        {
            System.Diagnostics.Debug.WriteLine($"{flags}: {message}");

            return true;
        }

        private ImageView CreateImageView(Image image, Format format, ImageAspectFlags aspectFlags)
        {
            return device.CreateImageView(image, ImageViewType.ImageView2d, format, ComponentMapping.Identity,
                new ImageSubresourceRange
                {
                    AspectMask = aspectFlags,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                });
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

            throw new Exception("No compatible memory type.");
        }

        private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
        {
            QueueFamilyIndices indices = new QueueFamilyIndices();

            var queueFamilies = device.GetQueueFamilyProperties();

            for (uint index = 0; index < queueFamilies.Length && !indices.IsComplete; index++)
            {
                if (queueFamilies[index].QueueFlags.HasFlag(QueueFlags.Graphics))
                {
                    indices.GraphicsFamily = index;
                }

                if (device.GetSurfaceSupport(index, this.surface))
                {
                    indices.PresentFamily = index;
                }

                if (queueFamilies[index].QueueFlags.HasFlag(QueueFlags.Transfer) && !queueFamilies[index].QueueFlags.HasFlag(QueueFlags.Graphics))
                {
                    indices.TransferFamily = index;
                }
            }

            if (!indices.TransferFamily.HasValue)
            {
                indices.TransferFamily = indices.GraphicsFamily;
            }

            return indices;
        }

        private SurfaceFormat ChooseSwapSurfaceFormat(SurfaceFormat[] availableFormats)
        {
            if (availableFormats.Length == 1 && availableFormats[0].Format == Format.Undefined)
            {
                return new SurfaceFormat
                {
                    Format = Format.B8G8R8A8UNorm,
                    ColorSpace = ColorSpace.SrgbNonlinear
                };
            }

            foreach (var format in availableFormats)
            {
                if (format.Format == Format.B8G8R8A8UNorm && format.ColorSpace == ColorSpace.SrgbNonlinear)
                {
                    return format;
                }
            }

            return availableFormats[0];
        }

        private PresentMode ChooseSwapPresentMode(PresentMode[] availablePresentModes)
        {
            return availablePresentModes.Contains(PresentMode.Mailbox)
                    ? PresentMode.Mailbox
                    : PresentMode.Fifo;
        }

        public Extent2D ChooseSwapExtent(SurfaceCapabilities capabilities)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                return capabilities.CurrentExtent;
            }
            else
            {
                return new Extent2D
                {
                    Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, (uint)this.glfwService.WindowWidth)),
                    Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, (uint)this.glfwService.WindowHeight))
                };
            }
        }

        SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice device)
        {
            return new SwapChainSupportDetails
            {
                Capabilities = device.GetSurfaceCapabilities(this.surface),
                Formats = device.GetSurfaceFormats(this.surface),
                PresentModes = device.GetSurfacePresentModes(this.surface)
            };
        }

        private bool IsSuitableDevice(PhysicalDevice device)
        {
            return device.EnumerateDeviceExtensionProperties(null).Any(extension => extension.ExtensionName == KhrExtensions.Swapchain)
                    && FindQueueFamilies(device).IsComplete;
        }

        internal struct QueueFamilyIndices
        {
            public uint? GraphicsFamily;
            public uint? PresentFamily;
            public uint? TransferFamily;

            public IEnumerable<uint> Indices
            {
                get
                {
                    if (this.GraphicsFamily.HasValue)
                    {
                        yield return this.GraphicsFamily.Value;
                    }

                    if (this.PresentFamily.HasValue && this.PresentFamily != this.GraphicsFamily)
                    {
                        yield return this.PresentFamily.Value;
                    }

                    if (this.TransferFamily.HasValue && this.TransferFamily != this.PresentFamily && this.TransferFamily != this.GraphicsFamily)
                    {
                        yield return this.TransferFamily.Value;
                    }
                }
            }

            public bool IsComplete
            {
                get
                {
                    return this.GraphicsFamily.HasValue
                        && this.PresentFamily.HasValue
                        && this.TransferFamily.HasValue;
                }
            }
        }

        private struct SwapChainSupportDetails
        {
            public SurfaceCapabilities Capabilities;
            public SurfaceFormat[] Formats;
            public PresentMode[] PresentModes;
        }

        public struct UniformBufferObject
        {
            public mat4 World;
            public mat4 View;
            public mat4 Projection;
        };
    }
}
