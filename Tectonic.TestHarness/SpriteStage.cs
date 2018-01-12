﻿using GlmSharp;
using SharpVk;
using SharpVk.Shanq;
using SharpVk.Shanq.GlmSharp;
using SharpVk.Spirv;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Tectonic
{
    public class SpriteStage
        : RenderStage
    {
        private ShaderModule vertexShader;
        private ShaderModule fragmentShader;
        private PipelineLayout pipelineLayout;
        private DescriptorSet descriptorSet;
        private Pipeline pipeline;
        private VulkanBuffer vertexBuffer;
        private VulkanBuffer indexBuffer;
        private VulkanBuffer spriteDataBuffer;

        private int indexCount;
        private float aspectRatio;
        private VulkanImage textureImage;
        private ImageView textureImageView;
        private Sampler textureSampler;
        private DescriptorPool descriptorPool;
        private DescriptorSetLayout descriptorSetLayout;

        private readonly Vertex[] vertices =
        {
            new Vertex(new vec2(0, 0), new vec2(0, 0)),
            new Vertex(new vec2(0, 1), new vec2(0, 1)),
            new Vertex(new vec2(1, 1), new vec2(1, 1)),
            new Vertex(new vec2(1, 0), new vec2(1, 0))
        };

        private readonly uint[] indices = { 0, 1, 2, 2, 3, 0 };

        private const int maxInstanceCount = 256 * 16;

        private int currentInstanceCount = 32 * 32;

        public override void Initialise(Device device, VulkanBufferManager bufferManager)
        {
            this.vertexShader = device.CreateVertexModule(shanq => from input in shanq.GetInput<Vertex>()
                                                                   from spriteData in shanq.GetInput<SpriteData>()
                                                                   select new VertexOutput
                                                                   {
                                                                       Uv = new vec2(input.Uv.x * spriteData.Size.x, input.Uv.y * spriteData.Size.y) + spriteData.AtlasPosition,
                                                                       Position = spriteData.Transform * new vec4(input.Position, 0, 1)
                                                                   });

            this.fragmentShader = device.CreateFragmentModule(shanq => from input in shanq.GetInput<FragmentInput>()
                                                                       from texture in shanq.GetSampler2d<vec4>(0, 0)
                                                                       let texColour = texture.Sample(input.Uv)
                                                                       select new FragmentOutput
                                                                       {
                                                                           Colour = texColour
                                                                       });

            indexCount = indices.Count();

            this.vertexBuffer = bufferManager.CreateBuffer((uint)Marshal.SizeOf<Vertex>() * (uint)vertices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal);

            this.vertexBuffer.Update(vertices);

            this.indexBuffer = bufferManager.CreateBuffer((uint)Marshal.SizeOf<uint>() * (uint)indices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal);

            this.indexBuffer.Update(indices);

            this.spriteDataBuffer = bufferManager.CreateBuffer((uint)Marshal.SizeOf<SpriteData>() * maxInstanceCount, BufferUsageFlags.TransferDestination | BufferUsageFlags.UniformBuffer, MemoryPropertyFlags.DeviceLocal);

            const string building = ".\\textures\\iso-64x64-building.png";
            const string square = ".\\textures\\square.png";

            var textureSource = SixLabors.ImageSharp.Image.Load(building);

            uint textureWidth = (uint)textureSource.Width;
            uint textureHeight = (uint)textureSource.Height;
            DeviceSize imageSize = textureWidth * textureHeight * 4;

            using (var stagingImage = bufferManager.CreateImage(textureWidth, textureHeight, Format.R8G8B8A8UNorm, ImageTiling.Linear, ImageUsageFlags.TransferSource, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent))
            {
                IntPtr memoryBuffer = stagingImage.Map();

                unsafe
                {
                    fixed (byte* rgbaPointer = textureSource.SavePixelData())
                    {
                        System.Buffer.MemoryCopy(rgbaPointer, memoryBuffer.ToPointer(), imageSize, imageSize);
                    }
                }

                stagingImage.Unmap();

                this.textureImage = bufferManager.CreateImage(textureWidth, textureHeight, Format.R8G8B8A8UNorm, ImageTiling.Optimal, ImageUsageFlags.TransferDestination | ImageUsageFlags.Sampled, MemoryPropertyFlags.DeviceLocal);

                stagingImage.TransitionImageLayout(ImageLayout.Preinitialized, ImageLayout.TransferSourceOptimal);
                this.textureImage.TransitionImageLayout(ImageLayout.Preinitialized, ImageLayout.TransferDestinationOptimal);

                stagingImage.Copy(this.textureImage, textureWidth, textureHeight);
                this.textureImage.TransitionImageLayout(ImageLayout.TransferDestinationOptimal, ImageLayout.ShaderReadOnlyOptimal);
            }

            this.textureImageView = device.CreateImageView(this.textureImage.Image,
                                                            ImageViewType.ImageView2d,
                                                            Format.R8G8B8A8UNorm,
                                                            ComponentMapping.Identity,
                                                            new ImageSubresourceRange(ImageAspectFlags.Color, 0, 1, 0, 1));

            this.textureSampler = device.CreateSampler(Filter.Nearest,
                                                        Filter.Nearest,
                                                        SamplerMipmapMode.Nearest,
                                                        SamplerAddressMode.ClampToBorder,
                                                        SamplerAddressMode.ClampToBorder,
                                                        SamplerAddressMode.ClampToBorder,
                                                        0,
                                                        false,
                                                        1,
                                                        false,
                                                        CompareOp.Always,
                                                        0,
                                                        0,
                                                        BorderColor.FloatTransparentBlack,
                                                        true);

            this.descriptorPool = device.CreateDescriptorPool(2,
                new[]
                {
                    new DescriptorPoolSize
                    {
                        DescriptorCount = 2,
                        Type = DescriptorType.UniformBuffer
                    },
                    new DescriptorPoolSize
                    {
                        DescriptorCount = 2,
                        Type = DescriptorType.CombinedImageSampler
                    }
                });

            this.descriptorSetLayout = device.CreateDescriptorSetLayout(
                new[]
                {
                    new DescriptorSetLayoutBinding
                    {
                        Binding = 0,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        StageFlags = ShaderStageFlags.Fragment
                    }
                });

            this.pipelineLayout = device.CreatePipelineLayout(this.descriptorSetLayout, null);

            this.descriptorSet = device.AllocateDescriptorSet(descriptorPool, this.descriptorSetLayout);

            device.UpdateDescriptorSets(new[]
            {
                new WriteDescriptorSet
                {
                    ImageInfo = new []
                    {
                        new DescriptorImageInfo
                        {
                            ImageView = textureImageView,
                            Sampler = textureSampler,
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
                        }
                    },
                    DescriptorCount = 1,
                    DestinationSet = this.descriptorSet,
                    DestinationBinding = 0,
                    DestinationArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler
                }
            }, null);
        }

        public override void Bind(Device device, RenderPass renderPass, CommandBuffer commandBuffer, Extent2D targetExtent)
        {
            this.aspectRatio = (float)targetExtent.Width / (float)targetExtent.Height;

            var size = new vec2(64, 64);

            var projection = mat4.Translate(0, -1, 0) * mat4.Scale(size.x / targetExtent.Width, size.y / targetExtent.Height, 1) * mat4.Scale(8);

            var rand = new Random();

            for (int layerIndex = 0; layerIndex < 1; layerIndex++)
            {
                this.spriteDataBuffer.Update(Enumerable.Range(0, 32).SelectMany(x => Enumerable.Range(0, 32).Select(y => (x, y))).Select(coord =>
                        new SpriteData
                        {
                            AtlasPosition = new vec2(0, 0) * size,
                            Size = size,
                            Transform = projection * mat4.Translate(((coord.x - coord.y) * 0.5f) - 0.5f, ((coord.x + coord.y) * 0.25f) - (0.5f * layerIndex) - 3, 0)
                        }).ToArray(), layerIndex * 256);
            }

            //this.spriteDataBuffer.Update(Enumerable.Range(0, 16).SelectMany(x => Enumerable.Range(0, 16).Select(y => (x, y))).Select(coord =>
            //        new SpriteData
            //        {
            //            AtlasPosition = new vec2(6 + rand.Next(4), 6) * size,
            //            Size = size,
            //            Transform = projection * mat4.Translate(-0.5f + ((coord.x - coord.y) * 0.5f), ((coord.x + coord.y) * 0.25f), 0.001f * (32 - (coord.x + coord.y)))
            //        }).ToArray(), 256);

            this.pipeline = device.CreateGraphicsPipeline(null,
                new[]
                {
                    new PipelineShaderStageCreateInfo
                    {
                        Stage = ShaderStageFlags.Vertex,
                        Module = this.vertexShader,
                        Name = "main"
                    },
                    new PipelineShaderStageCreateInfo
                    {
                        Stage = ShaderStageFlags.Fragment,
                        Module = this.fragmentShader,
                        Name = "main"
                    }
                },
                new PipelineVertexInputStateCreateInfo()
                {
                    VertexBindingDescriptions = new[] { Vertex.GetBindingDescription(), SpriteData.GetBindingDescription() },
                    VertexAttributeDescriptions = Vertex.GetAttributeDescriptions().Concat(SpriteData.GetAttributeDescriptions()).ToArray()
                },
                new PipelineInputAssemblyStateCreateInfo
                {
                    Topology = PrimitiveTopology.TriangleList
                },
                new PipelineRasterizationStateCreateInfo
                {
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1,
                    CullMode = CullModeFlags.Back,
                    FrontFace = FrontFace.CounterClockwise
                },
                this.pipelineLayout,
                renderPass,
                0,
                null,
                0,
                viewportState: new PipelineViewportStateCreateInfo
                {
                    Viewports = new[]
                        {
                            new Viewport
                            {
                                X = 0f,
                                Y = 0f,
                                Width = targetExtent.Width,
                                Height = targetExtent.Height,
                                MaxDepth = 1,
                                MinDepth = 0
                            }
                        },
                    Scissors = new[]
                        {
                            new Rect2D(targetExtent)
                        }
                },
                multisampleState: new PipelineMultisampleStateCreateInfo
                {
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.SampleCount1,
                    MinSampleShading = 1
                },
                depthStencilState: new PipelineDepthStencilStateCreateInfo
                {
                    DepthTestEnable = false,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.Less,
                    MinDepthBounds = 0,
                    MaxDepthBounds = 1
                },
                colorBlendState: new PipelineColorBlendStateCreateInfo
                {
                    Attachments = new[]
                        {
                            new PipelineColorBlendAttachmentState
                            {
                                ColorWriteMask = ColorComponentFlags.R
                                                    | ColorComponentFlags.G
                                                    | ColorComponentFlags.B
                                                    | ColorComponentFlags.A,
                                BlendEnable = true,
                                SourceColorBlendFactor = BlendFactor.SourceAlpha,
                                DestinationColorBlendFactor = BlendFactor.OneMinusSourceAlpha,
                                ColorBlendOp = BlendOp.Add,
                                SourceAlphaBlendFactor = BlendFactor.One,
                                DestinationAlphaBlendFactor = BlendFactor.One,
                                AlphaBlendOp = BlendOp.Max
                            }
                        },
                    BlendConstants = new float[] { 0, 0, 0, 0 }
                });

            commandBuffer.BindPipeline(PipelineBindPoint.Graphics, this.pipeline);

            commandBuffer.BindVertexBuffers(0, new[] { this.vertexBuffer.Buffer, this.spriteDataBuffer.Buffer }, new DeviceSize[] { 0, 0 });

            commandBuffer.BindIndexBuffer(this.indexBuffer.Buffer, 0, IndexType.Uint32);

            commandBuffer.BindDescriptorSets(PipelineBindPoint.Graphics, pipelineLayout, 0, this.descriptorSet, null);

            commandBuffer.DrawIndexed((uint)indexCount, (uint)currentInstanceCount, 0, 0, 0);
        }

        private static uint[] LoadShaderData(string filePath, out int codeSize)
        {
            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            var shaderData = new uint[(int)Math.Ceiling(fileBytes.Length / 4f)];

            System.Buffer.BlockCopy(fileBytes, 0, shaderData, 0, fileBytes.Length);

            codeSize = fileBytes.Length;

            return shaderData;
        }

        private struct VertexOutput
        {
            [Location(0)]
            public vec2 Uv;

            [BuiltIn(BuiltIn.Position)]
            public vec4 Position;
        }

        private struct FragmentInput
        {
            [Location(0)]
            public vec2 Uv;
        }

        private struct FragmentOutput
        {
            [Location(0)]
            public vec4 Colour;
        }

        private struct Vertex
        {
            public Vertex(vec2 position, vec2 uv)
            {
                this.Position = position;
                this.Uv = uv;
            }

            [Location(0)]
            public vec2 Position;

            [Location(1)]
            public vec2 Uv;

            public static VertexInputBindingDescription GetBindingDescription()
            {
                return new VertexInputBindingDescription()
                {
                    Binding = 0,
                    Stride = (uint)Marshal.SizeOf<Vertex>(),
                    InputRate = VertexInputRate.Vertex
                };
            }

            public static VertexInputAttributeDescription[] GetAttributeDescriptions()
            {
                return new VertexInputAttributeDescription[]
                {
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 0,
                        Format = Format.R32G32SFloat,
                        Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Position))
                    },
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 1,
                        Format = Format.R32G32SFloat,
                        Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Uv))
                    }
                };
            }
        }

        private struct SpriteData
        {
            [Location(2)]
            public vec2 Size;

            [Location(3)]
            public vec2 AtlasPosition;

            [Location(4)]
            public mat4 Transform;

            public static VertexInputBindingDescription GetBindingDescription()
            {
                return new VertexInputBindingDescription()
                {
                    Binding = 1,
                    Stride = (uint)Marshal.SizeOf<SpriteData>(),
                    InputRate = VertexInputRate.Instance
                };
            }

            public static IEnumerable<VertexInputAttributeDescription> GetAttributeDescriptions()
            {
                yield return new VertexInputAttributeDescription(2, 1, Format.R32G32SFloat, (uint)Marshal.OffsetOf<SpriteData>(nameof(Size)));

                yield return new VertexInputAttributeDescription(3, 1, Format.R32G32SFloat, (uint)Marshal.OffsetOf<SpriteData>(nameof(AtlasPosition)));

                uint transformOffset = (uint)Marshal.OffsetOf<SpriteData>(nameof(Transform));
                int rowSize = Marshal.SizeOf<vec4>();

                for (int elementIndex = 0; elementIndex < 4; elementIndex++)
                {
                    yield return new VertexInputAttributeDescription((uint)(4 + elementIndex), 1, Format.R32G32B32A32SFloat, (uint)(transformOffset + elementIndex * rowSize));
                }
            }
        };
    }
}
