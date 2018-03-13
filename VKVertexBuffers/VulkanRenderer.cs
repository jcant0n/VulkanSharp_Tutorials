﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Vulkan;
using Vulkan.Windows;

namespace VKVertexBuffers
{
    public class VulkanRenderer
    {
        private const uint VK_SUBPASS_EXTERNAL = ~0U;
        private int width, height;

        private Instance vkInstance;
        private SurfaceKhr vkSurface;
        private PhysicalDevice vkPhysicalDevice;
        private Device vkDevice;
        private Queue vkGraphicsQueue;
        private Queue vkPresentQueue;
        private SwapchainKhr vkSwapChain;
        private Image[] vkSwapChainImages;
        private Format vkSwapChainImageFormat;
        private Extent2D vkSwapChainExtent;
        private ImageView[] vkSwapChainImageViews;
        private RenderPass vkRenderPass;
        private PipelineLayout vkPipelineLayout;
        private Pipeline vkGraphicsPipeline;
        private Framebuffer[] vkSwapChainFramebuffers;
        private CommandPool vkCommandPool;
        private CommandBuffer[] vkCommandBuffers;
        private Semaphore vkImageAvailableSemaphore;
        private Semaphore vkRenderFinishedSemaphore;
        private Vulkan.Buffer vkVertexBuffer;
        private DeviceMemory vkVertexBufferMemory;
        private Vulkan.Buffer vkIndexBuffer;
        private DeviceMemory vkIndexBufferMemory;

        public readonly Vertex[] vertices =
        {
            new Vertex(new Vector2(-0.5f, -0.5f), new Vector3(1.0f, 0.0f, 0.0f)),
            new Vertex(new Vector2(0.5f, -0.5f), new Vector3(0.0f, 1.0f, 0.0f)),
            new Vertex(new Vector2(0.5f,  0.5f), new Vector3(0.0f, 0.0f, 1.0f)),
            new Vertex(new Vector2(-0.5f,  0.5f), new Vector3(1.0f, 1.0f, 1.0f)),
        };

        public readonly short[] indices = {
            0, 1, 2, 2, 3, 0,
        };

        public void InitVulkan(IntPtr windowsHandle, int width, int height)
        {
            this.width = width;
            this.height = height;

            CreateInstance();
            CreateSurface(windowsHandle);
            CreatePhysicalDevice();
            CreateLogicalDevice();
            CreateSwapChain();
            CreateImageViews();
            CreateRenderPass();
            CreateGraphicsPipeline();
            CreateFrameBuffers();
            CreateCommandPool();
            CreateVertexBuffer();
            CreateIndexBuffer();
            CreateCommandBuffers();
            CreateSemaphores();
        }

        public void DrawFrame()
        {
            vkPresentQueue.WaitIdle();

            uint imageIndex = vkDevice.AcquireNextImageKHR(vkSwapChain, ulong.MaxValue, vkImageAvailableSemaphore);

            var submitInfo = new SubmitInfo()
            {
                WaitSemaphores = new Semaphore[] { vkImageAvailableSemaphore },
                WaitDstStageMask = new PipelineStageFlags[] { PipelineStageFlags.ColorAttachmentOutput },
                CommandBuffers = new CommandBuffer[] { vkCommandBuffers[imageIndex] },
                SignalSemaphores = new Semaphore[] { vkRenderFinishedSemaphore },
            };

            vkGraphicsQueue.Submit(submitInfo);

            var presentInfo = new PresentInfoKhr()
            {
                WaitSemaphores = new Semaphore[] { vkRenderFinishedSemaphore },
                Swapchains = new SwapchainKhr[] { vkSwapChain },
                ImageIndices = new uint[] { imageIndex },
            };

            vkPresentQueue.PresentKHR(presentInfo);
        }

        public void CleanUp()
        {
            vkDevice.WaitIdle();

            vkDevice.DestroyBuffer(vkVertexBuffer);
            vkDevice.FreeMemory(vkVertexBufferMemory);
            vkDevice.DestroyBuffer(vkIndexBuffer);
            vkDevice.FreeMemory(vkIndexBufferMemory);

            vkDevice.DestroySemaphore(vkImageAvailableSemaphore);
            vkDevice.DestroySemaphore(vkRenderFinishedSemaphore);

            vkDevice.DestroyCommandPool(vkCommandPool);

            foreach (var framebuffer in vkSwapChainFramebuffers)
            {
                vkDevice.DestroyFramebuffer(framebuffer);
            }

            vkDevice.DestroyPipeline(vkGraphicsPipeline);
            vkDevice.DestroyPipelineLayout(vkPipelineLayout);
            vkDevice.DestroyRenderPass(vkRenderPass);

            foreach (var imageview in vkSwapChainImageViews)
            {
                vkDevice.DestroyImageView(imageview);
            }

            vkDevice.DestroySwapchainKHR(vkSwapChain);
            vkDevice.Destroy();
            vkInstance.DestroySurfaceKHR(vkSurface);
            vkInstance.Destroy();
        }

        private void CreateInstance()
        {
            ApplicationInfo appInfo = new ApplicationInfo()
            {
                ApplicationName = "Hello Triangle",
                EngineName = "WaveEngine",
                EngineVersion = Vulkan.Version.Make(3, 0, 0),
            };

            string[] extensions = new string[]
            {
                "VK_KHR_surface",
                "VK_KHR_win32_surface",
            };

            InstanceCreateInfo createInfo = new InstanceCreateInfo()
            {
                EnabledExtensionNames = extensions,
                ApplicationInfo = appInfo,
            };

            vkInstance = new Instance(createInfo);
        }

        private void CreateSurface(IntPtr windowHandle)
        {
            vkSurface = vkInstance.CreateWin32SurfaceKHR(
                            new Win32SurfaceCreateInfoKhr()
                            {
                                Hinstance = Process.GetCurrentProcess().Handle,
                                Hwnd = windowHandle
                            });
        }

        private void CreatePhysicalDevice()
        {
            var physicalDevices = vkInstance.EnumeratePhysicalDevices();
            if (physicalDevices.Length == 0)
            {
                throw new InvalidOperationException("No devices with vulkan support available.");
            }

            foreach (var device in physicalDevices)
            {
                var features = device.GetFeatures();
                var properties = device.GetProperties();

                if (properties.DeviceType == PhysicalDeviceType.DiscreteGpu)
                {
                    vkPhysicalDevice = device;
                    break;
                }
            }
        }

        private void CreateLogicalDevice()
        {
            var indices = new QueueFamilyIndices(vkPhysicalDevice, vkSurface);

            var queueCreateInfos = new DeviceQueueCreateInfo[]
            {
                new DeviceQueueCreateInfo()
                {
                    QueueFamilyIndex = (uint)indices.GraphicsFamily,
                    QueueCount = 1,
                    QueuePriorities = new float[] { 1.0f },
                }
            };

            var createInfo = new DeviceCreateInfo()
            {
                EnabledExtensionNames = new string[] { "VK_KHR_swapchain" },
                QueueCreateInfos = queueCreateInfos,
                EnabledFeatures = new PhysicalDeviceFeatures(),
            };

            vkDevice = vkPhysicalDevice.CreateDevice(createInfo);

            vkGraphicsQueue = vkDevice.GetQueue((uint)indices.GraphicsFamily, 0);
            vkPresentQueue = vkDevice.GetQueue((uint)indices.PresentFamily, 0);
        }

        private void CreateSwapChain()
        {
            var swapChainSupport = new SwapChainSupportDetails(vkPhysicalDevice, vkSurface);

            SurfaceFormatKhr surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.formats);
            PresentModeKhr presentMode = ChooseSwapPresentMode(swapChainSupport.presentModes);
            Extent2D extent = ChooseSwapExtent(swapChainSupport.capabilities);

            uint imageCount = swapChainSupport.capabilities.MinImageCount + 1;
            if (swapChainSupport.capabilities.MaxImageCount > 0)
            {
                imageCount = Math.Min(imageCount, swapChainSupport.capabilities.MaxImageCount);
            }

            var createInfo = new SwapchainCreateInfoKhr()
            {
                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachment,
                PreTransform = swapChainSupport.capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKhr.Opaque,
                PresentMode = presentMode,
                Surface = vkSurface,
            };

            var indices = new QueueFamilyIndices(vkPhysicalDevice, vkSurface);
            if (indices.GraphicsFamily != indices.PresentFamily)
            {
                createInfo.ImageSharingMode = SharingMode.Concurrent;
                createInfo.QueueFamilyIndices = new[]
                {
                    (uint)indices.GraphicsFamily,
                    (uint)indices.PresentFamily,
                };
            }
            else
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
            }

            vkSwapChain = vkDevice.CreateSwapchainKHR(createInfo);
            vkSwapChainImages = vkDevice.GetSwapchainImagesKHR(vkSwapChain);
            vkSwapChainImageFormat = surfaceFormat.Format;
            vkSwapChainExtent = extent;
        }

        private SurfaceFormatKhr ChooseSwapSurfaceFormat(SurfaceFormatKhr[] formats)
        {
            if (formats.Length == 1 && formats[0].Format == Format.Undefined)
            {
                return new SurfaceFormatKhr() { Format = Format.B8G8R8A8Unorm, ColorSpace = ColorSpaceKhr.SrgbNonlinear };
            }

            foreach (var availableFormat in formats)
            {
                if (availableFormat.Format == Format.B8G8R8A8Unorm && availableFormat.ColorSpace == ColorSpaceKhr.SrgbNonlinear)
                {
                    return availableFormat;
                }
            }

            return formats[0];
        }

        private PresentModeKhr ChooseSwapPresentMode(PresentModeKhr[] presentModes)
        {
            PresentModeKhr bestMode = PresentModeKhr.Fifo;

            foreach (var availablePresentMode in presentModes)
            {
                if (availablePresentMode == PresentModeKhr.Mailbox)
                {
                    return availablePresentMode;
                }
                else if (availablePresentMode == PresentModeKhr.Immediate)
                {
                    bestMode = PresentModeKhr.Immediate;
                }
            }

            return bestMode;
        }

        private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKhr capabilities)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                return capabilities.CurrentExtent;
            }

            return new Extent2D()
            {
                Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, (uint)this.width)),
                Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, (uint)this.height)),
            };
        }

        private void CreateImageViews()
        {
            vkSwapChainImageViews = new ImageView[vkSwapChainImages.Length];

            for (int i = 0; i < vkSwapChainImages.Length; i++)
            {
                var image = vkSwapChainImages[i];
                ImageViewCreateInfo createInfo = new ImageViewCreateInfo()
                {
                    Image = image,
                    ViewType = ImageViewType.View2D,
                    Format = vkSwapChainImageFormat,
                    Components = new ComponentMapping()
                    {
                        R = ComponentSwizzle.Identity,
                        G = ComponentSwizzle.Identity,
                        B = ComponentSwizzle.Identity,
                        A = ComponentSwizzle.Identity
                    },
                    SubresourceRange = new ImageSubresourceRange()
                    {
                        AspectMask = ImageAspectFlags.Color,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };

                vkSwapChainImageViews[i] = vkDevice.CreateImageView(createInfo);
            }
        }

        private void CreateRenderPass()
        {
            var colorAttachment = new AttachmentDescription()
            {
                Format = vkSwapChainImageFormat,
                Samples = SampleCountFlags.Count1,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = Vulkan.ImageLayout.Undefined,
                FinalLayout = Vulkan.ImageLayout.PresentSrcKhr,
            };

            var colorAttachmentRef = new AttachmentReference()
            {
                Attachment = 0,
                Layout = Vulkan.ImageLayout.ColorAttachmentOptimal,
            };

            var subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                ColorAttachments = new AttachmentReference[] { colorAttachmentRef },
            };

            var dependency = new SubpassDependency()
            {
                SrcSubpass = VK_SUBPASS_EXTERNAL,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutput,
                SrcAccessMask = 0,

                DstSubpass = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutput,
                DstAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite,
            };

            var renderPassInfo = new RenderPassCreateInfo()
            {
                AttachmentCount = 1,
                Attachments = new AttachmentDescription[] { colorAttachment },
                SubpassCount = 1,
                Subpasses = new SubpassDescription[] { subpass },
                DependencyCount = 1,
                Dependencies = new SubpassDependency[] { dependency },
            };

            vkRenderPass = vkDevice.CreateRenderPass(renderPassInfo);
        }

        private void CreateGraphicsPipeline()
        {
            // Shader stages
            var vertShaderCode = System.IO.File.ReadAllBytes("Shaders/vert.spv");
            var fragShaderCode = System.IO.File.ReadAllBytes("Shaders/frag.spv");

            var vertShaderModule = vkDevice.CreateShaderModule(vertShaderCode);
            var fragShaderModule = vkDevice.CreateShaderModule(fragShaderCode);

            var vertShaderStageInfo = new PipelineShaderStageCreateInfo()
            {
                Stage = ShaderStageFlags.Vertex,
                Module = vertShaderModule,
                Name = "main",
            };

            var fragShaderStageInfo = new PipelineShaderStageCreateInfo()
            {
                Stage = ShaderStageFlags.Fragment,
                Module = fragShaderModule,
                Name = "main",
            };

            var shaderStages = new PipelineShaderStageCreateInfo[] { vertShaderStageInfo, fragShaderStageInfo };

            // VertexInput
            var vertexInputInfo = new PipelineVertexInputStateCreateInfo()
            {
                VertexBindingDescriptions = Vertex.GetBindingDescription(),
                VertexAttributeDescriptions = Vertex.GetAttributeDescription(),
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo()
            {
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = false,
            };

            var viewport = new Viewport()
            {
                X = 0f,
                Y = 0f,
                Width = (float)vkSwapChainExtent.Width,
                Height = (float)vkSwapChainExtent.Height,
                MinDepth = 0f,
                MaxDepth = 1f,
            };

            var scissor = new Rect2D()
            {
                Offset = new Offset2D() { X = 0, Y = 0 },
                Extent = vkSwapChainExtent,
            };

            var viewportState = new PipelineViewportStateCreateInfo()
            {
                ViewportCount = 1,
                Viewports = new Viewport[] { viewport },
                ScissorCount = 1,
                Scissors = new Rect2D[] { scissor },
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo()
            {
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1f,
                CullMode = CullModeFlags.Back,
                FrontFace = FrontFace.Clockwise,
                DepthBiasEnable = false,
                DepthBiasConstantFactor = 0f,
                DepthBiasClamp = 0f,
                DepthBiasSlopeFactor = 0f,
            };

            var multisampling = new PipelineMultisampleStateCreateInfo()
            {
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1,
                MinSampleShading = 1f,
                SampleMask = null,
                AlphaToCoverageEnable = false,
                AlphaToOneEnable = false,
            };

            var colorBlendAttachmend = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.R | ColorComponentFlags.G | ColorComponentFlags.B | ColorComponentFlags.A,
                BlendEnable = false,
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo()
            {
                LogicOpEnable = false,
                LogicOp = LogicOp.Copy,
                Attachments = new PipelineColorBlendAttachmentState[] { colorBlendAttachmend },
                BlendConstants = new float[] { 0f, 0f, 0f, 0f },
            };

            var pipelineLayoutInfo = new PipelineLayoutCreateInfo()
            {
                SetLayoutCount = 0,
                PushConstantRangeCount = 0,
            };

            vkPipelineLayout = vkDevice.CreatePipelineLayout(pipelineLayoutInfo);

            var pipelineInfo = new GraphicsPipelineCreateInfo()
            {
                StageCount = 2,
                Stages = shaderStages,
                VertexInputState = vertexInputInfo,
                InputAssemblyState = inputAssembly,
                ViewportState = viewportState,
                RasterizationState = rasterizer,
                MultisampleState = multisampling,
                DepthStencilState = null,
                ColorBlendState = colorBlending,
                DynamicState = null,
                Layout = vkPipelineLayout,
                RenderPass = vkRenderPass,
                Subpass = 0,
                BasePipelineHandle = null,
                BasePipelineIndex = -1,
            };

            vkGraphicsPipeline = vkDevice.CreateGraphicsPipelines(null, new GraphicsPipelineCreateInfo[] { pipelineInfo })[0];
        }

        private void CreateFrameBuffers()
        {
            vkSwapChainFramebuffers = new Framebuffer[vkSwapChainImageViews.Length];

            for (int i = 0; i < vkSwapChainImageViews.Length; i++)
            {
                var attachments = new ImageView[] { vkSwapChainImageViews[i] };

                var frameBufferInfo = new FramebufferCreateInfo()
                {
                    RenderPass = vkRenderPass,
                    AttachmentCount = 1,
                    Attachments = attachments,
                    Width = vkSwapChainExtent.Width,
                    Height = vkSwapChainExtent.Height,
                    Layers = 1,
                };

                vkSwapChainFramebuffers[i] = vkDevice.CreateFramebuffer(frameBufferInfo);
            }
        }

        private void CreateCommandPool()
        {
            var indices = new QueueFamilyIndices(vkPhysicalDevice, vkSurface);

            var poolInfo = new CommandPoolCreateInfo()
            {
                QueueFamilyIndex = (uint)indices.GraphicsFamily,
                Flags = 0,
            };

            vkCommandPool = vkDevice.CreateCommandPool(poolInfo);
        }
        
        private void CreateVertexBuffer()
        {
            DeviceSize bufferSize = 5 * sizeof(float) * 4;

            CreateBuffer(
                bufferSize,
                BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
                out Vulkan.Buffer stagingVertexBuffer,
                out DeviceMemory stagingVertexBufferMemory
            );

            IntPtr bufferPointer = vkDevice.MapMemory(stagingVertexBufferMemory, 0, bufferSize);
            var byteArray = new float[vertices.Length * 5];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].Position.CopyTo(byteArray, i * 5);
                vertices[i].Color.CopyTo(byteArray, i * 5 + 2);
            }

            Marshal.Copy(byteArray, 0, bufferPointer, byteArray.Length);
            vkDevice.UnmapMemory(stagingVertexBufferMemory);

            CreateBuffer(
                bufferSize,
                BufferUsageFlags.VertexBuffer | BufferUsageFlags.TransferDst,
                MemoryPropertyFlags.DeviceLocal,
                out vkVertexBuffer,
                out vkVertexBufferMemory);

            CopyBuffer(stagingVertexBuffer, vkVertexBuffer, bufferSize);

            vkDevice.DestroyBuffer(stagingVertexBuffer);
            vkDevice.FreeMemory(stagingVertexBufferMemory);
        }

        private void CreateIndexBuffer()
        {
            DeviceSize bufferSize = sizeof(short) * (uint)indices.Length;

            CreateBuffer(
                bufferSize,
                BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
                out Vulkan.Buffer stagingIndexBuffer,
                out DeviceMemory stagingIndexBufferMemory);

            IntPtr bufferPointer = vkDevice.MapMemory(stagingIndexBufferMemory, 0, bufferSize);
            Marshal.Copy(indices, 0, bufferPointer, indices.Length);
            vkDevice.UnmapMemory(stagingIndexBufferMemory);

            CreateBuffer(
                bufferSize,
                BufferUsageFlags.IndexBuffer | BufferUsageFlags.TransferDst,
                MemoryPropertyFlags.DeviceLocal,
                out vkIndexBuffer,
                out vkIndexBufferMemory);

            CopyBuffer(stagingIndexBuffer, vkIndexBuffer, bufferSize);

            vkDevice.DestroyBuffer(stagingIndexBuffer);
            vkDevice.FreeMemory(stagingIndexBufferMemory);
        }

        private void CreateBuffer(DeviceSize size, BufferUsageFlags usageFlags, MemoryPropertyFlags properties, out Vulkan.Buffer buffer, out DeviceMemory bufferMemory)
        {
            var bufferInfo = new BufferCreateInfo()
            {
                Size = size,
                Usage = usageFlags,
                SharingMode = SharingMode.Exclusive,
            };

            buffer = vkDevice.CreateBuffer(bufferInfo);

            var memRequirements = vkDevice.GetBufferMemoryRequirements(buffer);

            var allocInfo = new MemoryAllocateInfo()
            {
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties),
            };

            bufferMemory = vkDevice.AllocateMemory(allocInfo);

            vkDevice.BindBufferMemory(buffer, bufferMemory, 0);
        }

        private void CopyBuffer(Vulkan.Buffer srcBuffer, Vulkan.Buffer dstBuffer, DeviceSize size)
        {
            var allocInfo = new CommandBufferAllocateInfo()
            {
                CommandPool = vkCommandPool,
                CommandBufferCount = 1,
                Level = CommandBufferLevel.Primary,
            };

            var commmandBuffer = vkDevice.AllocateCommandBuffers(allocInfo)[0];

            commmandBuffer.Begin(new CommandBufferBeginInfo() { Flags = CommandBufferUsageFlags.OneTimeSubmit });
            commmandBuffer.CmdCopyBuffer(srcBuffer, dstBuffer, new BufferCopy[] { new BufferCopy() { Size = size } });
            commmandBuffer.End();

            vkGraphicsQueue.Submit(new SubmitInfo() { CommandBuffers = new CommandBuffer[] { commmandBuffer } });
            vkGraphicsQueue.WaitIdle();

            vkDevice.FreeCommandBuffer(vkCommandPool, commmandBuffer);
        }

        private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
        {
            var memProperties = vkPhysicalDevice.GetMemoryProperties();

            for (var i = 0; i < memProperties.MemoryTypeCount; ++i)
            {
                if ((typeFilter & (1 << i)) != 0
                    && (memProperties.MemoryTypes[i].PropertyFlags & properties) != 0)
                {
                    return (uint)i;
                }
            }

            throw new InvalidOperationException("Failed to find suitable memory type");
        }

        private void CreateCommandBuffers()
        {
            var allocInfo = new CommandBufferAllocateInfo()
            {
                CommandPool = vkCommandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)vkSwapChainFramebuffers.Length,
            };

            vkCommandBuffers = vkDevice.AllocateCommandBuffers(allocInfo);

            for (int i = 0; i < vkCommandBuffers.Length; i++)
            {
                var beginInfo = new CommandBufferBeginInfo()
                {
                    Flags = CommandBufferUsageFlags.SimultaneousUse,
                    InheritanceInfo = null,
                };

                var buffer = vkCommandBuffers[i];
                buffer.Begin(beginInfo);

                var renderPassInfo = new RenderPassBeginInfo()
                {
                    RenderPass = vkRenderPass,
                    Framebuffer = vkSwapChainFramebuffers[i],
                    RenderArea = new Rect2D() { Extent = vkSwapChainExtent },
                    ClearValues = new ClearValue[] { new ClearValue() { Color = new ClearColorValue(new float[] { 0.0f, 0.0f, 0.0f, 1.0f }) } },
                };

                buffer.CmdBeginRenderPass(renderPassInfo, SubpassContents.Inline);
                buffer.CmdBindPipeline(PipelineBindPoint.Graphics, vkGraphicsPipeline);
                buffer.CmdBindVertexBuffer(0, vkVertexBuffer, 0);
                buffer.CmdBindIndexBuffer(vkIndexBuffer, 0, IndexType.Uint16);
                buffer.CmdDrawIndexed((uint)indices.Length, 1, 0, 0, 0);
                buffer.CmdEndRenderPass();
                buffer.End();
            }
        }

        private void CreateSemaphores()
        {
            var semaphoreInfo = new SemaphoreCreateInfo();

            vkImageAvailableSemaphore = vkDevice.CreateSemaphore(semaphoreInfo);
            vkRenderFinishedSemaphore = vkDevice.CreateSemaphore(semaphoreInfo);
        }
    }
}
