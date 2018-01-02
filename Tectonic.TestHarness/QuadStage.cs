using GlmSharp;
using SharpVk;
using SharpVk.Shanq;
using SharpVk.Shanq.GlmSharp;
using SharpVk.Spirv;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Tectonic
{
    public class QuadStage
        : RenderStage
    {
        private ShaderModule vertexShader;
        private ShaderModule fragmentShader;
        private PipelineLayout pipelineLayout;
        private Pipeline pipeline;
        private VulkanBuffer vertexBuffer;
        private VulkanBuffer indexBuffer;

        private int indexCount;
        private float aspectRatio;


        private readonly Vertex[] vertices =
        {
            new Vertex(new vec2(-1f, -1f), new vec3(1.0f, 0.0f, 0.0f)),
            new Vertex(new vec2(1f, -1f), new vec3(0.0f, 1.0f, 0.0f)),
            new Vertex(new vec2(1f, 1f), new vec3(0.0f, 0.0f, 1.0f)),
            new Vertex(new vec2(-1f, 1f), new vec3(1.0f, 1.0f, 1.0f))
        };

        private readonly uint[] indices = { 0, 1, 2, 2, 3, 0 };

        public override void Initialise(Device device, VulkanBufferManager bufferManager)
        {
            this.vertexShader = device.CreateVertexModule(shanq => from input in shanq.GetInput<Vertex>()
                                                                   select new VertexOutput
                                                                   {
                                                                       Colour = input.Colour,
                                                                       Position = new vec4(input.Position, 0, 1)
                                                                   });

            this.fragmentShader = device.CreateFragmentModule(shanq => from input in shanq.GetInput<FragmentInput>()
                                                                       select new FragmentOutput
                                                                       {
                                                                           Colour = new vec4(input.Colour, 1)
                                                                       });

            indexCount = indices.Count();

            this.vertexBuffer = bufferManager.CreateBuffer((uint)Marshal.SizeOf<Vertex>() * (uint)vertices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal);

            this.vertexBuffer.Update(vertices);

            this.indexBuffer = bufferManager.CreateBuffer((uint)Marshal.SizeOf<uint>() * (uint)indices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal);

            this.indexBuffer.Update(indices);

            this.pipelineLayout = device.CreatePipelineLayout(null, null);
        }

        public override void Bind(Device device, RenderPass renderPass, CommandBuffer commandBuffer, Extent2D targetExtent)
        {
            this.aspectRatio = (float)targetExtent.Width / (float)targetExtent.Height;

            this.pipeline = device.CreateGraphicsPipelines(null, new[]
            {
                new GraphicsPipelineCreateInfo
                {
                    Layout = this.pipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0,
                    VertexInputState = new PipelineVertexInputStateCreateInfo()
                    {
                        VertexBindingDescriptions = new [] { Vertex.GetBindingDescription() },
                        VertexAttributeDescriptions = Vertex.GetAttributeDescriptions()
                    },
                    InputAssemblyState = new PipelineInputAssemblyStateCreateInfo
                    {
                        PrimitiveRestartEnable = false,
                        Topology = PrimitiveTopology.TriangleList
                    },
                    ViewportState = new PipelineViewportStateCreateInfo
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
                            new Rect2D
                            {
                                Offset = new Offset2D(),
                                Extent= targetExtent
                            }
                        }
                    },
                    RasterizationState = new PipelineRasterizationStateCreateInfo
                    {
                        DepthClampEnable = false,
                        RasterizerDiscardEnable = false,
                        PolygonMode = PolygonMode.Fill,
                        LineWidth = 1,
                        CullMode = CullModeFlags.None,
                        FrontFace = FrontFace.CounterClockwise,
                        DepthBiasEnable = false
                    },
                    MultisampleState = new PipelineMultisampleStateCreateInfo
                    {
                        SampleShadingEnable = false,
                        RasterizationSamples = SampleCountFlags.SampleCount1,
                        MinSampleShading = 1
                    },
                    ColorBlendState = new PipelineColorBlendStateCreateInfo
                    {
                        Attachments = new[]
                        {
                            new PipelineColorBlendAttachmentState
                            {
                                ColorWriteMask = ColorComponentFlags.R
                                                    | ColorComponentFlags.G
                                                    | ColorComponentFlags.B
                                                    | ColorComponentFlags.A,
                                BlendEnable = false,
                                SourceColorBlendFactor = BlendFactor.One,
                                DestinationColorBlendFactor = BlendFactor.Zero,
                                ColorBlendOp = BlendOp.Add,
                                SourceAlphaBlendFactor = BlendFactor.One,
                                DestinationAlphaBlendFactor = BlendFactor.Zero,
                                AlphaBlendOp = BlendOp.Add
                            }
                        },
                        LogicOpEnable = false,
                        LogicOp = LogicOp.Copy,
                        BlendConstants = new float[] {0,0,0,0}
                    },
                    DepthStencilState = new PipelineDepthStencilStateCreateInfo
                    {
                        DepthTestEnable = true,
                        DepthWriteEnable = true,
                        DepthCompareOp = CompareOp.Less,
                        DepthBoundsTestEnable = false,
                        MinDepthBounds = 0,
                        MaxDepthBounds = 1,
                        StencilTestEnable = false
                    },
                    Stages = new[]
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
                    }
                }
            }).Single();

            commandBuffer.BindPipeline(PipelineBindPoint.Graphics, this.pipeline);

            commandBuffer.BindVertexBuffers(0, this.vertexBuffer.Buffer, (DeviceSize)0);

            commandBuffer.BindIndexBuffer(this.indexBuffer.Buffer, 0, IndexType.Uint32);

            commandBuffer.DrawIndexed((uint)indexCount, 1, 0, 0, 0);
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
            public vec3 Colour;

            [BuiltIn(BuiltIn.Position)]
            public vec4 Position;
        }

        private struct FragmentInput
        {
            [Location(0)]
            public vec3 Colour;
        }

        private struct FragmentOutput
        {
            [Location(0)]
            public vec4 Colour;
        }

        private struct Vertex
        {
            public Vertex(vec2 position, vec3 colour)
            {
                this.Position = position;
                this.Colour = colour;
            }

            [Location(0)]
            public vec2 Position;

            [Location(1)]
            public vec3 Colour;

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
                        Offset = (uint)Marshal.OffsetOf<Vertex>("Position")
                    },
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 1,
                        Format = Format.R32G32B32SFloat,
                        Offset = (uint)Marshal.OffsetOf<Vertex>("Colour")
                    }
                };
            }
        }

        public struct UniformBufferObject
        {
            public mat4 World;
            public mat4 View;
            public mat4 Projection;
            public mat4 InvTransWorld;
        };
    }
}
