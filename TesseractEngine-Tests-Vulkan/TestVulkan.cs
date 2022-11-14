using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using Tesseract.Core.Numerics;
using Tesseract.Core.Native;
using Tesseract.Core.Utilities;
using Tesseract.Tests.GLFW;
using Tesseract.GLFW;
using Tesseract.Vulkan;

namespace Tesseract.Tests.Vulkan {
	
	public static class TestVulkan {

		private class GLFWVulkanLoader : IVKLoader {

			public IntPtr GetVKProcAddress(string name) {
				return GLFW3.Functions.glfwGetInstanceProcAddress(IntPtr.Zero, name);
			}

		}

		private static VKInstance CreateInstance(VK vk) {
			using MemoryStack sp = MemoryStack.Push();
			using ManagedPointer<VKApplicationInfo> pAppInfo = new(new VKApplicationInfo() {
				Type = VKStructureType.ApplicationInfo,
				APIVersion = VK10.ApiVersion,
				ApplicationName = "TesseractEngine-Test",
				ApplicationVersion = VK10.MakeApiVersion(0, 1, 0, 0),
				EngineName = "Tesseract",
				EngineVersion = VK10.MakeApiVersion(0, 1, 0, 0)
			});

			HashSet<string> layers = new(), extensions = new();
			extensions.Add(KHRSurface.ExtensionName);
			extensions.Add(EXTDebugReport.ExtensionName);
			layers.Add("VK_LAYER_KHRONOS_validation");
			//layers.Add("VK_LAYER_LUNARG_api_dump");

			foreach (string ext in GLFW3.RequiredInstanceExtensions) extensions.Add(ext);

			Console.WriteLine("[Vulkan] Instance Extensions:");
			foreach (string ext in extensions) Console.WriteLine($"[Vulkan]     {ext}");
			Console.WriteLine("[Vulkan] Instance Layers:");
			foreach (string lyr in layers) Console.WriteLine($"[Vulkan]     {lyr}");

			return vk.CreateInstance(new VKInstanceCreateInfo() {
				Type = VKStructureType.InstanceCreateInfo,
				ApplicationInfo = pAppInfo,
				EnabledLayerCount = (uint)layers.Count,
				EnabledLayerNames = sp.UTF8Array(layers),
				EnabledExtensionCount = (uint)extensions.Count,
				EnabledExtensionNames = sp.UTF8Array(extensions)
			});
		}

		private static VKSurfaceKHR CreateSurface(VKInstance instance, GLFWWindow window) {
			VK.CheckError((VKResult)GLFW3.Functions.glfwCreateWindowSurface(instance, window.Window, IntPtr.Zero, out ulong surface), "Failed to create window surface");
			return new VKSurfaceKHR(instance, surface, null);
		}

		private static VKDevice CreateDevice(VKInstance instance, VKSurfaceKHR surface, out VKPhysicalDevice physicalDevice, out VKQueue queue, out int queueFamily) {
			using MemoryStack sp = MemoryStack.Push();

			physicalDevice = instance.PhysicalDevices[0];

			var physicalDeviceProperties = physicalDevice.Properties;

			Console.WriteLine($"[Vulkan] Physical Device \"{physicalDeviceProperties.DeviceName}\"");

			queueFamily = -1;
			var queueFamilies = physicalDevice.QueueFamilyProperties;
			for (int family = 0; family < queueFamilies.Length; family++) {
				var queueInfo = queueFamilies[family];
				if ((queueInfo.QueueFlags & VKQueueFlagBits.Graphics) != 0 && physicalDevice.GetSurfaceSupportKHR((uint)family, surface)) {
					queueFamily = family;
					break;
				}
			}

			if (queueFamily == -1) throw new VulkanException("Physical device does not have graphics queue which supports surface!");

			Console.WriteLine($"[Vulkan] Queue Family: {queueFamily}");

			HashSet<string> extensions = new();
			extensions.Add(KHRSwapchain.ExtensionName);

			Console.WriteLine("[Vulkan] Device Extensions:");
			foreach (string ext in extensions) Console.WriteLine($"[Vulkan]     {ext}");

			VKDevice device = physicalDevice.CreateDevice(new VKDeviceCreateInfo() {
				Type = VKStructureType.DeviceCreateInfo,
				QueueCreateInfoCount = 1,
				QueueCreateInfos = sp.Values(new VKDeviceQueueCreateInfo() {
					Type = VKStructureType.DeviceQueueCreateInfo,
					QueueCount = 1,
					QueueFamilyIndex = (uint)queueFamily,
					QueuePriorities = sp.Values(1.0f)
				}),
				EnabledExtensionCount = (uint)extensions.Count,
				EnabledExtensionNames = sp.UTF8Array(extensions)
			});

			queue = device.GetQueue((uint)queueFamily, 0);

			return device;
		}

		private static int ScoreFormat(VKSurfaceFormatKHR format) {
			int score = 0;
			if (format.ColorSpace == VKColorSpaceKHR.SRGBNonlinear) score += 1;
			if (format.Format == VKFormat.R8G8B8A8UNorm) score += 1;
			return score;
		}

		private static int ScorePresentMode(VKPresentModeKHR mode) => mode switch {
			VKPresentModeKHR.Immediate => -10,
			VKPresentModeKHR.FIFO => 5,
			VKPresentModeKHR.FIFORelaxed => 10,
			VKPresentModeKHR.Mailbox => 20,
			_ => 0
		};

		private static VKSwapchainKHR CreateSwapchain(VKPhysicalDevice physicalDevice, VKDevice device, VKSurfaceKHR surface, GLFWWindow window, out VKFormat imageFormat, out Vector2ui extent, VKSwapchainKHR oldSwapchain = null) {
			using MemoryStack sp = MemoryStack.Push();

			VKSurfaceCapabilitiesKHR capabilities = physicalDevice.GetSurfaceCapabilitiesKHR(surface);
			VKSurfaceFormatKHR[] formats = physicalDevice.GetSurfaceFormatsKHR(surface);
			VKPresentModeKHR[] presentModes = physicalDevice.GetSurfacePresentModesKHR(surface);

			extent = (Vector2ui)window.FramebufferSize;
			extent.X = Math.Min(Math.Max(extent.X, capabilities.MinImageExtent.X), capabilities.MaxImageExtent.X);
			extent.Y = Math.Min(Math.Max(extent.X, capabilities.MinImageExtent.Y), capabilities.MaxImageExtent.Y);

			uint imageCount = Math.Min(Math.Max(2, capabilities.MinImageCount), capabilities.MaxImageCount);

			VKSurfaceFormatKHR format = (from fmt in formats
										 let score = ScoreFormat(fmt)
										 orderby score descending
										 select fmt).First();

			VKPresentModeKHR presentMode = (from mode in presentModes
											let score = ScorePresentMode(mode)
											orderby score descending
											select mode).First();

			imageFormat = format.Format;

			Console.WriteLine("[Vulkan] Swapchain (re)creation:");
			Console.WriteLine($"[Vulkan]     Format: {format.Format}");
			Console.WriteLine($"[Vulkan]     ColorSpace: {format.ColorSpace}");
			Console.WriteLine($"[Vulkan]     Image Count: {imageCount}");
			Console.WriteLine($"[Vulkan]     Present Mode: {presentMode}");
			Console.WriteLine($"[Vulkan]     Size: {extent}");

			return device.CreateSwapchainKHR(new VKSwapchainCreateInfoKHR() {
				Type = VKStructureType.SwapchainCreateInfoKHR,
				Surface = surface,
				ImageExtent = extent,
				ImageArrayLayers = 1,
				ImageFormat = format.Format,
				ImageSharingMode = VKSharingMode.Exclusive,
				ImageColorSpace = format.ColorSpace,
				ImageUsage = VKImageUsageFlagBits.TransferDst,
				MinImageCount = imageCount,
				CompositeAlpha = VKCompositeAlphaFlagBitsKHR.Opaque,
				Clipped = true,
				PreTransform = capabilities.CurrentTransform,
				PresentMode = presentMode,
				OldSwapchain = oldSwapchain
			});
		}

		private static bool ShouldRecreateSurface(VKPhysicalDevice physicalDevice, VKSurfaceKHR surface) {
			VKSurfaceCapabilitiesKHR capabilities = physicalDevice.GetSurfaceCapabilitiesKHR(surface);
			return (Vector2ui)capabilities.CurrentExtent != default;
		}

		private class SwapchainImage : IDisposable {

			public VKImage Image { get; }
			public VKFence Fence { get; }

			public SwapchainImage(VKDevice device, VKImage image, VKFormat format) {
				Image = image;
				Fence = device.CreateFence(new VKFenceCreateInfo() {
					Type = VKStructureType.FenceCreateInfo,
					Flags = VKFenceCreateFlagBits.Signaled
				});
			}

			public void Dispose() {
				GC.SuppressFinalize(this);
				Fence.Dispose();
			}

		}

		private class SemaphoreManager : IDisposable {

			private readonly VKDevice device;
			private readonly List<VKSemaphore> semaphores = new();
			private int next = 0;

			public SemaphoreManager(VKDevice device) {
				this.device = device;
			}

			public void Rewind() => next = 0;

			public VKSemaphore Next() {
				if (next >= semaphores.Count) {
					semaphores.Add(device.CreateSemaphore(new VKSemaphoreCreateInfo() {
						Type = VKStructureType.SemaphoreCreateInfo
					}));
				}
				return semaphores[next++];
			}

			public void Dispose() {
				foreach (VKSemaphore sem in semaphores) sem.Dispose();
			}

		}

		private class CommandManager : IDisposable {

			private readonly VKDevice device;
			private readonly VKCommandPool commandPool;
			private readonly List<(VKCommandBuffer, VKFence)> commands = new();

			public CommandManager(VKDevice device, int queueFamily) {
				this.device = device;
				commandPool = device.CreateCommandPool(new VKCommandPoolCreateInfo() {
					Type = VKStructureType.CommandPoolCreateInfo,
					QueueFamilyIndex = (uint)queueFamily,
					Flags = VKCommandPoolCreateFlagBits.ResetCommandBuffer
				});
			}

			public (VKCommandBuffer, VKFence) Next() {
				foreach(var cmd in commands) {
					if (cmd.Item2.Status) {
						cmd.Item2.Reset();
						cmd.Item1.Reset();
						return cmd;
					}
				}
				VKCommandBuffer cmdbuf = commandPool.Allocate(new VKCommandBufferAllocateInfo() {
					Type = VKStructureType.CommandBufferAllocateInfo,
					CommandBufferCount = 1,
					CommandPool = commandPool,
					Level = VKCommandBufferLevel.Primary
				})[0];
				VKFence fence = device.CreateFence(new VKFenceCreateInfo() { Type = VKStructureType.FenceCreateInfo });
				commands.Add((cmdbuf, fence));
				return (cmdbuf, fence);
			}

			public void Dispose() {
				foreach(var cmd in commands) {
					cmd.Item1.Dispose();
					cmd.Item2.Dispose();
				}
				commandPool.Dispose();
			}

		}

		private static VKShaderModule CreateShaderModule(VKDevice device, string name) {
			Assembly asm = Assembly.GetAssembly(typeof(TestVulkan));
			using Stream res = asm.GetManifestResourceStream($"Tesseract.Tests.Vulkan.Shaders.{name}.spv");
			using MemoryStream ms = new();
			res.CopyTo(ms);
			using ManagedPointer<byte> code = new(ms.ToArray());
			return device.CreateShaderModule(new VKShaderModuleCreateInfo() {
				Type = VKStructureType.ShaderModuleCreateInfo,
				Code = code,
				CodeSize = (nuint)code.ArraySize
			});
		}

		private class PipelineImpl : IDisposable {

			public VKShaderModule VertexShader { get; }
			public VKShaderModule FragmentShader { get; }
			public VKDescriptorSetLayout DescriptorSetLayout { get; }
			public VKPipelineLayout PipelineLayout { get; }
			public VKRenderPass RenderPass { get; }
			public VKPipeline Pipeline { get; }

			public PipelineImpl(VKDevice device, VKFormat swapchainImageFormat) {
				using MemoryStack sp = MemoryStack.Push();
				VertexShader = CreateShaderModule(device, "main.vert");
				FragmentShader = CreateShaderModule(device, "main.frag");
				DescriptorSetLayout = device.CreateDescriptorSetLayout(new VKDescriptorSetLayoutCreateInfo() {
					Type = VKStructureType.DescriptorSetLayoutCreateInfo,
					BindingCount = 1,
					Bindings = sp.Values(new VKDescriptorSetLayoutBinding() {
						Binding = 0,
						DescriptorType = VKDescriptorType.CombinedImageSampler,
						DescriptorCount = 1,
						StageFlags = VKShaderStageFlagBits.Fragment
					})
				});
				PipelineLayout = device.CreatePipelineLayout(new VKPipelineLayoutCreateInfo() {
					Type = VKStructureType.PipelineLayoutCreateInfo,
					SetLayoutCount = 1,
					SetLayouts = sp.Values(DescriptorSetLayout)
				});
				RenderPass = device.CreateRenderPass(new VKRenderPassCreateInfo() {
					Type = VKStructureType.RenderPassCreateInfo,
					AttachmentCount = 1,
					Attachments = sp.Values(new VKAttachmentDescription() {
						Format = swapchainImageFormat,
						InitialLayout = VKImageLayout.Undefined,
						FinalLayout = VKImageLayout.TransferSrcOptimal,
						LoadOp = VKAttachmentLoadOp.Clear,
						StoreOp = VKAttachmentStoreOp.Store,
						Samples = VKSampleCountFlagBits.Count1Bit
					}),
					SubpassCount = 1,
					Subpasses = sp.Values(new VKSubpassDescription() {
						PipelineBindPoint = VKPipelineBindPoint.Graphics,
						ColorAttachmentCount = 1,
						ColorAttachments = sp.Values(new VKAttachmentReference() {
							Attachment = 0,
							Layout = VKImageLayout.ColorAttachmentOptimal
						})
					})
				});
				using ManagedPointer<VKPipelineShaderStageCreateInfo> shaderStages = new(
					new VKPipelineShaderStageCreateInfo() {
						Type = VKStructureType.PipelineShaderStageCreateInfo,
						Module = VertexShader,
						Stage = VKShaderStageFlagBits.Vertex,
						Name = "main"
					},
					new VKPipelineShaderStageCreateInfo() {
						Type = VKStructureType.PipelineShaderStageCreateInfo,
						Module = FragmentShader,
						Stage = VKShaderStageFlagBits.Fragment,
						Name = "main"
					}
				);
				Pipeline = device.CreateGraphicsPipelines(null, new VKGraphicsPipelineCreateInfo() {
					Type = VKStructureType.GraphicsPipelineCreateInfo,
					Layout = PipelineLayout,
					RenderPass = RenderPass,
					Subpass = 0,

					StageCount = (uint)shaderStages.ArraySize,
					Stages = shaderStages,
					VertexInputState = sp.Values(new VKPipelineVertexInputStateCreateInfo() {
						Type = VKStructureType.PipelineVertexInputStateCreateInfo,
						VertexAttributeDescriptionCount = 3,
						VertexAttributeDescriptions = sp.Values(
						new VKVertexInputAttributeDescription() {
							Binding = 0,
							Format = VKFormat.R32G32B32SFloat,
							Location = 0,
							Offset = 0
						}, new VKVertexInputAttributeDescription() {
							Binding = 0,
							Format = VKFormat.R32G32B32SFloat,
							Location = 1,
							Offset = 3 * sizeof(float)
						}, new VKVertexInputAttributeDescription() {
							Binding = 0,
							Format = VKFormat.R32G32SFloat,
							Location = 2,
							Offset = 6 * sizeof(float)
						}),
						VertexBindingDescriptionCount = 1,
						VertexBindingDescriptions = sp.Values(new VKVertexInputBindingDescription() {
							Binding = 0,
							InputRate = VKVertexInputRate.Vertex,
							Stride = 8 * sizeof(float)
						})
					}),
					InputAssemblyState = sp.Values(new VKPipelineInputAssemblyStateCreateInfo() {
						Type = VKStructureType.PipelineInputAssemblyStateCreateInfo,
						PrimitiveRestartEnable = false,
						Topology = VKPrimitiveTopology.TriangleList
					}),
					ViewportState = sp.Values(new VKPipelineViewportStateCreateInfo() {
						Type = VKStructureType.PipelineViewportStateCreateInfo,
						ScissorCount = 1,
						ViewportCount = 1
					}),
					RasterizationState = sp.Values(new VKPipelineRasterizationStateCreateInfo() {
						Type = VKStructureType.PipelineRasterizationStateCreateInfo,
						CullMode = VKCullModeFlagBits.None,
						FrontFace = VKFrontFace.Clockwise,
						PolygonMode = VKPolygonMode.Fill,
						LineWidth = 1.0f
					}),
					MultisampleState = sp.Values(new VKPipelineMultisampleStateCreateInfo() {
						Type = VKStructureType.PipelineMultisampleStateCreateInfo,
						RasterizationSamples = VKSampleCountFlagBits.Count1Bit
					}),
					ColorBlendState = sp.Values(new VKPipelineColorBlendStateCreateInfo() {
						Type = VKStructureType.PipelineColorBlendStateCreateInfo,
						AttachmentCount = 1,
						Attachments = sp.Values(new VKPipelineColorBlendAttachmentState() {
							BlendEnable = true,
							SrcColorBlendFactor = VKBlendFactor.One,
							DstColorBlendFactor = VKBlendFactor.Zero,
							ColorBlendOp = VKBlendOp.Add,
							SrcAlphaBlendFactor = VKBlendFactor.One,
							DstAlphaBlendFactor = VKBlendFactor.Zero,
							AlphaBlendOp = VKBlendOp.Add,
							ColorWriteMask = VKColorComponentFlagBits.R | VKColorComponentFlagBits.G | VKColorComponentFlagBits.B | VKColorComponentFlagBits.A
						})
					}),
					DynamicState = sp.Values(new VKPipelineDynamicStateCreateInfo() {
						Type = VKStructureType.PipelineDynamicStateCreateInfo,
						DynamicStateCount = 2,
						DynamicStates = sp.Values(VKDynamicState.Viewport, VKDynamicState.Scissor)
					})
				});
			}

			public void Dispose() {
				GC.SuppressFinalize(this);
				Pipeline.Dispose();
				RenderPass.Dispose();
				PipelineLayout.Dispose();
				DescriptorSetLayout.Dispose();
				FragmentShader.Dispose();
				VertexShader.Dispose();
			}

		}

		private static VKDeviceMemory AllocateMemory(VKDevice device, VKPhysicalDevice physicalDevice, VKMemoryRequirements mreqs, VKMemoryPropertyFlagBits reqFlags) {
			VKPhysicalDeviceMemoryProperties memprops = physicalDevice.MemoryProperties;
			int memoryType = -1;
			for(int i = 0; i < memprops.MemoryTypeCount; i++) {
				var memtype = memprops.MemoryTypes[i];
				if (((mreqs.MemoryTypeBits >> i) & 1) != 1) continue;
				if ((memtype.PropertyFlags & reqFlags) != reqFlags) continue;
				memoryType = i;
				break;
			}
			if (memoryType == -1) throw new VulkanException("Could not find memory type that fits requirements");
			return device.AllocateMemory(new VKMemoryAllocateInfo() {
				Type = VKStructureType.MemoryAllocateInfo,
				AllocationSize = mreqs.Size,
				MemoryTypeIndex = (uint)memoryType
			});
		}

		private static VKBuffer CreateBuffer(VKDevice device, VKPhysicalDevice physicalDevice, int size, VKBufferUsageFlagBits usage, VKMemoryPropertyFlagBits reqMemFlags, out VKDeviceMemory memory) {
			VKBuffer buffer = device.CreateBuffer(new VKBufferCreateInfo() {
				Type = VKStructureType.BufferCreateInfo,
				SharingMode = VKSharingMode.Exclusive,
				Size = (ulong)size,
				Usage = usage
			});
			memory = AllocateMemory(device, physicalDevice, buffer.MemoryRequirements, reqMemFlags);
			buffer.BindMemory(memory, 0);
			return buffer;
		}

		private static VKImage CreateImage(VKDevice device, VKPhysicalDevice physicalDevice, int width, int height, VKFormat format, VKImageUsageFlagBits usage, out VKDeviceMemory memory) {
			VKImage image = device.CreateImage(new VKImageCreateInfo() {
				Type = VKStructureType.ImageCreateInfo,
				ImageType = VKImageType.Type2D,
				Format = format,
				Extent = new Vector3ui((uint)width, (uint)height, 1),
				MipLevels = 1,
				ArrayLayers = 1,
				Samples = VKSampleCountFlagBits.Count1Bit,
				Tiling = VKImageTiling.Optimal,
				Usage = usage,
				SharingMode = VKSharingMode.Exclusive,
				InitialLayout = VKImageLayout.Undefined
			});
			memory = AllocateMemory(device, physicalDevice, image.MemoryRequirements, 0);
			image.BindMemory(memory, 0);
			return image;
		}

		private static void UploadTexture(CommandManager cm, VKDevice device, VKQueue queue, VKBuffer src, VKImage dst, VKBufferImageCopy copy, VKImageLayout finalLayout = VKImageLayout.ShaderReadOnlyOptimal, VKPipelineStageFlagBits dstStage = VKPipelineStageFlagBits.FragmentShader, VKAccessFlagBits dstAccess = VKAccessFlagBits.ShaderRead) {
			using MemoryStack sp = MemoryStack.Push();
			var textureUploadCommands = cm.Next();
			var cmdbuf = textureUploadCommands.Item1;

			cmdbuf.Begin(new VKCommandBufferBeginInfo() {
				Type = VKStructureType.CommandBufferBeginInfo
			});

			cmdbuf.PipelineBarrier(
				VKPipelineStageFlagBits.TopOfPipe,
				VKPipelineStageFlagBits.Transfer,
				0,
				stackalloc VKMemoryBarrier[0],
				stackalloc VKBufferMemoryBarrier[0],
				stackalloc VKImageMemoryBarrier[] {
				new() {
					Type = VKStructureType.ImageMemoryBarrier,
					Image = dst,
					OldLayout = VKImageLayout.Undefined,
					NewLayout = VKImageLayout.TransferDstOptimal,
					SrcAccessMask = 0,
					DstAccessMask = VKAccessFlagBits.TransferWrite,
					SrcQueueFamilyIndex = VK10.QueueFamilyIgnored,
					DstQueueFamilyIndex = VK10.QueueFamilyIgnored,
					SubresourceRange = new() {
						AspectMask = VKImageAspectFlagBits.Color,
						BaseArrayLayer = 0,
						BaseMipLevel = 0,
						LayerCount = 1,
						LevelCount = 1
					}
				}
			}
			);

			cmdbuf.CopyBufferToImage(src, dst, VKImageLayout.TransferDstOptimal, new VKBufferImageCopy() {
				ImageSubresource = new() {
					AspectMask = VKImageAspectFlagBits.Color,
					BaseArrayLayer = 0,
					LayerCount = 1,
					MipLevel = 0
				},
				ImageExtent = new(32, 32, 1)
			});

			cmdbuf.PipelineBarrier(
				VKPipelineStageFlagBits.Transfer,
				dstStage,
				0,
				stackalloc VKMemoryBarrier[0],
				stackalloc VKBufferMemoryBarrier[0],
				stackalloc VKImageMemoryBarrier[] {
				new() {
					Type = VKStructureType.ImageMemoryBarrier,
					Image = dst,
					OldLayout = VKImageLayout.TransferDstOptimal,
					NewLayout = finalLayout,
					SrcAccessMask = VKAccessFlagBits.TransferWrite,
					DstAccessMask = dstAccess,
					SrcQueueFamilyIndex = VK10.QueueFamilyIgnored,
					DstQueueFamilyIndex = VK10.QueueFamilyIgnored,
					SubresourceRange = new() {
						AspectMask = VKImageAspectFlagBits.Color,
						BaseArrayLayer = 0,
						BaseMipLevel = 0,
						LayerCount = 1,
						LevelCount = 1
					}
				}
			}
			);

			cmdbuf.End();

			queue.Submit(new VKSubmitInfo() {
				Type = VKStructureType.SubmitInfo,
				CommandBufferCount = 1,
				CommandBuffers = sp.Values(cmdbuf.CommandBuffer)
			}, textureUploadCommands.Item2);
			textureUploadCommands.Item2.WaitFor(ulong.MaxValue);
		}

		public struct Vertex {

			public Vector3 Position;

			public Vector3 Color;

			public Vector2 TexCoord;

		}

		private class FramebufferImpl : IDisposable {

			public VKFormat ColorFormat { get; }
			public Vector2ui Extent { get; }

			public VKImage ColorAttachment { get; }
			private readonly VKDeviceMemory colorAttachmentMemory;
			public VKImageView ColorAttachmentView { get; }

			public VKFramebuffer Framebuffer { get; }

			public FramebufferImpl(VKDevice device, VKPhysicalDevice physicalDevice, PipelineImpl pipeline, VKFormat colorFormat, Vector2ui extent) {
				using MemoryStack sp = MemoryStack.Push();
				ColorFormat = colorFormat;
				Extent = extent;

				ColorAttachment = CreateImage(device, physicalDevice, (int)extent.X, (int)extent.Y, colorFormat, VKImageUsageFlagBits.ColorAttachment | VKImageUsageFlagBits.TransferSrc, out colorAttachmentMemory);
				ColorAttachmentView = device.CreateImageView(new VKImageViewCreateInfo() {
					Type = VKStructureType.ImageViewCreateInfo,
					Image = ColorAttachment,
					ViewType = VKImageViewType.Type2D,
					Format = colorFormat,
					Components = new() { R = VKComponentSwizzle.Identity, G = VKComponentSwizzle.Identity, B = VKComponentSwizzle.Identity, A = VKComponentSwizzle.Identity },
					SubresourceRange = new() {
						AspectMask = VKImageAspectFlagBits.Color,
						BaseArrayLayer = 0,
						BaseMipLevel = 0,
						LayerCount = 1,
						LevelCount = 1
					}
				});

				Framebuffer = device.CreateFramebuffer(new VKFramebufferCreateInfo() {
					Type = VKStructureType.FramebufferCreateInfo,
					RenderPass = pipeline.RenderPass,
					Width = extent.X,
					Height = extent.Y,
					Layers = 1,
					AttachmentCount = 1,
					Attachments = sp.Values(ColorAttachmentView)
				});
			}

			public void Dispose() {
				Framebuffer.Dispose();

				ColorAttachmentView.Dispose();
				ColorAttachment.Dispose();
				colorAttachmentMemory.Dispose();
			}

		}

		private static void RecordCommands(VKCommandBuffer cmdbuf, VKBuffer vbo, VKDescriptorSet descriptorSet, PipelineImpl pipeline, FramebufferImpl framebuffer, SwapchainImage swapchainImage) {
			using MemoryStack sp = MemoryStack.Push();

			// Begin
			cmdbuf.Begin(new VKCommandBufferBeginInfo() {
				Type = VKStructureType.CommandBufferBeginInfo,
				Flags = VKCommandBufferUsageFlagBits.OneTimeSubmit
			});

			// Transition to TransferDst layout
			cmdbuf.PipelineBarrier(
				VKPipelineStageFlagBits.TopOfPipe,
				VKPipelineStageFlagBits.Transfer,
				0,
				stackalloc VKMemoryBarrier[0],
				stackalloc VKBufferMemoryBarrier[0],
				stackalloc VKImageMemoryBarrier[] {
				new() {
					Type = VKStructureType.ImageMemoryBarrier,
					Image = swapchainImage.Image,
					OldLayout = VKImageLayout.Undefined,
					NewLayout = VKImageLayout.TransferDstOptimal,
					SrcAccessMask = 0,
					DstAccessMask = VKAccessFlagBits.TransferWrite,
					SrcQueueFamilyIndex = VK10.QueueFamilyIgnored,
					DstQueueFamilyIndex = VK10.QueueFamilyIgnored,
					SubresourceRange = new() {
						AspectMask = VKImageAspectFlagBits.Color,
						BaseArrayLayer = 0,
						BaseMipLevel = 0,
						LayerCount = 1,
						LevelCount = 1
					}
				}
			}
			);

			// Begin render pass
			cmdbuf.BeginRenderPass(new VKRenderPassBeginInfo() {
				Type = VKStructureType.RenderPassBeginInfo,
				Framebuffer = framebuffer.Framebuffer,
				RenderPass = pipeline.RenderPass,
				RenderArea = new() { Extent = framebuffer.Extent },
				ClearValueCount = 1,
				ClearValues = sp.Values(new VKClearValue() {
					Color = new() { Float32 = new Vector4(0, 0, 0, 1) }
				})
			}, VKSubpassContents.Inline);

			// Setup state
			cmdbuf.BindPipeline(VKPipelineBindPoint.Graphics, pipeline.Pipeline);
			cmdbuf.BindVertexBuffers(0, vbo);
			cmdbuf.BindDescriptorSets(VKPipelineBindPoint.Graphics, pipeline.PipelineLayout, descriptorSet);
			cmdbuf.SetViewport(new VKViewport() { Width = framebuffer.Extent.X, Height = framebuffer.Extent.Y, MinDepth = 0.0f, MaxDepth = 1.0f });
			cmdbuf.SetScissor(new VKRect2D() { Extent = framebuffer.Extent });

			// Draw some vertices
			cmdbuf.Draw(3);

			// End render pass
			cmdbuf.EndRenderPass();

			// Copy framebuffer to swapchain
			cmdbuf.CopyImage(framebuffer.ColorAttachment, VKImageLayout.TransferSrcOptimal, swapchainImage.Image, VKImageLayout.TransferDstOptimal, new VKImageCopy() {
				Extent = new(framebuffer.Extent, 1),
				SrcSubresource = new() {
					AspectMask = VKImageAspectFlagBits.Color,
					BaseArrayLayer = 0,
					LayerCount = 1,
					MipLevel = 0
				},
				DstSubresource = new() {
					AspectMask = VKImageAspectFlagBits.Color,
					BaseArrayLayer = 0,
					LayerCount = 1,
					MipLevel = 0
				}
			});

			// Transition to PresentSrc
			cmdbuf.PipelineBarrier(
				VKPipelineStageFlagBits.Transfer,
				VKPipelineStageFlagBits.BottomOfPipe,
				0,
				stackalloc VKMemoryBarrier[0],
				stackalloc VKBufferMemoryBarrier[0],
				stackalloc VKImageMemoryBarrier[] {
				new() {
					Type = VKStructureType.ImageMemoryBarrier,
					Image = swapchainImage.Image,
					OldLayout = VKImageLayout.TransferDstOptimal,
					NewLayout = VKImageLayout.PresentSrcKHR,
					SrcAccessMask = VKAccessFlagBits.TransferWrite,
					DstAccessMask = VKAccessFlagBits.MemoryRead,
					SrcQueueFamilyIndex = VK10.QueueFamilyIgnored,
					DstQueueFamilyIndex = VK10.QueueFamilyIgnored,
					SubresourceRange = new() {
						AspectMask = VKImageAspectFlagBits.Color,
						BaseArrayLayer = 0,
						BaseMipLevel = 0,
						LayerCount = 1,
						LevelCount = 1
					}
				}
			}
			);

			// End
			cmdbuf.End();
		}

		public static void TestRaw() => TestGLFW.RunWithGLFW(() => {
			// Stack to more easily manage disposables
			Stack<IDisposable> disposables = new();

			GLFW3.DefaultWindowHints();
			GLFW3.WindowHint(GLFWWindowAttrib.Resizable, 0);
			GLFW3.WindowHint(GLFWWindowAttrib.ClientAPI, (int)GLFWClientAPI.NoAPI);
			GLFWWindow window = new(new Vector2i(800, 600), "Test Vulkan");
			disposables.Push(window);

			// Create instanace w/ debug callback
			VK vk = new(new GLFWVulkanLoader());
			VKInstance instance = CreateInstance(vk);
			disposables.Push(instance);
			VKDebugReportCallbackEXT debugReportCallback = instance.CreateDebugReportCallbackEXT(new VKDebugReportCallbackCreateInfoEXT() {
				Type = VKStructureType.DebugReportCallbackCreateInfoEXT,
				Flags = VKDebugReportFlagBitsEXT.Error | VKDebugReportFlagBitsEXT.Warning | VKDebugReportFlagBitsEXT.PerformanceWarning,
				Callback = (VKDebugReportFlagBitsEXT flags, VKDebugReportObjectTypeEXT objectType, ulong obj, nuint location, int messageCode, string layerPrefix, string message, IntPtr userData) => {
					if ((flags & VKDebugReportFlagBitsEXT.Error) != 0) Console.Error.WriteLine($"[Vulkan][{layerPrefix}]: {message}");
					else Console.WriteLine($"[Vulkan][{layerPrefix}]: {message}");
					return false;
				}
			});
			disposables.Push(debugReportCallback);

			// Create surface from window
			VKSurfaceKHR surface = CreateSurface(instance, window);
			disposables.Push(surface);

			// Create logical device & get queue
			VKDevice device = CreateDevice(instance, surface, out VKPhysicalDevice physicalDevice, out VKQueue queue, out int queueFamily);
			disposables.Push(device);

			// Create swapchain & get images
			VKSwapchainKHR swapchain = CreateSwapchain(physicalDevice, device, surface, window, out VKFormat swapchainImageFormat, out Vector2ui swapchainImageExtent);
			// Don't push swapchain as disposable, it has special handling
			bool rebuildSwapchain = false;
			List<SwapchainImage> swapchainImages = new();
			foreach (VKImage img in swapchain.Images) swapchainImages.Add(new SwapchainImage(device, img, swapchainImageFormat));

			// Create managers
			SemaphoreManager sm = new(device);
			disposables.Push(sm);
			CommandManager cm = new(device, queueFamily);
			disposables.Push(cm);

			// Create pipeline
			PipelineImpl pipeline = new(device, swapchainImageFormat);
			disposables.Push(pipeline);

			// Create vertex buffer
			Span<Vertex> vertices = stackalloc Vertex[3] {
				new Vertex() { Position = new(-1, -1, 0), Color = new(1, 0, 0), TexCoord = new(0, 0) },
				new Vertex() { Position = new(0, 1, 0), Color = new(0, 1, 0), TexCoord = new(0.5f, 1) },
				new Vertex() { Position = new(1, -1, 0), Color = new(0, 0, 1), TexCoord = new(1, 0) }
			};
			int vboSize = Marshal.SizeOf<Vertex>() * vertices.Length;
			VKBuffer vbo = CreateBuffer(device, physicalDevice, vboSize, VKBufferUsageFlagBits.VertexBuffer, VKMemoryPropertyFlagBits.HostVisible | VKMemoryPropertyFlagBits.HostCoherent, out VKDeviceMemory vboMemory);
			disposables.Push(vboMemory);
			disposables.Push(vbo);
			vertices.CopyTo(new UnmanagedPointer<Vertex>(vboMemory.MapMemory(0, VK10.WholeSize, 0), vertices.Length).Span);
			vboMemory.UnmapMemory();

			// Create texture
			VKImage texture = CreateImage(device, physicalDevice, 32, 32, VKFormat.R8G8B8A8UNorm, VKImageUsageFlagBits.TransferDst | VKImageUsageFlagBits.Sampled, out VKDeviceMemory textureMemory);
			disposables.Push(textureMemory);
			disposables.Push(texture);

			// Generate and upload pattern
			uint[] texturePixels = new uint[32 * 32];
			bool pixelState = false;
			for (int i = 0; i < texturePixels.Length; i++) {
				if ((i % 32) == 0) pixelState = !pixelState;
				texturePixels[i] = pixelState ? 0xFFFFFFFF : 0xFFCFCFCF;
				pixelState = !pixelState;
			}

			int pboSize = texturePixels.Length * sizeof(uint);
			VKBuffer pbo = CreateBuffer(device, physicalDevice, pboSize, VKBufferUsageFlagBits.TransferSrc, VKMemoryPropertyFlagBits.HostVisible | VKMemoryPropertyFlagBits.HostCoherent, out VKDeviceMemory pboMemory);
			disposables.Push(pboMemory);
			disposables.Push(pbo);
			texturePixels.CopyTo(new UnmanagedPointer<uint>(pboMemory.MapMemory(0, VK10.WholeSize, 0), texturePixels.Length).Span);
			pboMemory.UnmapMemory();

			UploadTexture(cm, device, queue, pbo, texture, new VKBufferImageCopy() {
				ImageSubresource = new() {
					AspectMask = VKImageAspectFlagBits.Color,
					BaseArrayLayer = 0,
					LayerCount = 1,
					MipLevel = 0
				},
				ImageExtent = new(32, 32, 1)
			});

			// Create texture view
			VKImageView textureView = device.CreateImageView(new VKImageViewCreateInfo() {
				Type = VKStructureType.ImageViewCreateInfo,
				Image = texture,
				ViewType = VKImageViewType.Type2D,
				Format = VKFormat.R8G8B8A8UNorm,
				Components = new VKComponentMapping() { R = VKComponentSwizzle.Identity, G = VKComponentSwizzle.Identity, B = VKComponentSwizzle.Identity, A = VKComponentSwizzle.Identity },
				SubresourceRange = new() {
					AspectMask = VKImageAspectFlagBits.Color,
					BaseArrayLayer = 0,
					BaseMipLevel = 0,
					LayerCount = 1,
					LevelCount = 1
				}
			});
			disposables.Push(textureView);

			// Create sampler
			VKSampler sampler = device.CreateSampler(new VKSamplerCreateInfo() {
				Type = VKStructureType.SamplerCreateInfo,
				AddressModeU = VKSamplerAddressMode.ClampToBorder,
				AddressModeV = VKSamplerAddressMode.ClampToBorder,
				AddressModeW = VKSamplerAddressMode.ClampToBorder,
				BorderColor = VKBorderColor.FloatTransparentBlack,
				MagFilter = VKFilter.Nearest,
				MinFilter = VKFilter.Nearest,
				MipmapMode = VKSamplerMipmapMode.Nearest
			});
			disposables.Push(sampler);

			// Create descriptor pool
			VKDescriptorPool descriptorPool;
			{
				using MemoryStack sp = MemoryStack.Push();
				descriptorPool = device.CreateDescriptorPool(new VKDescriptorPoolCreateInfo() {
					Type = VKStructureType.DescriptorPoolCreateInfo,
					MaxSets = 1,
					PoolSizeCount = 1,
					PoolSizes = sp.Values(new VKDescriptorPoolSize() {
						Type = VKDescriptorType.CombinedImageSampler,
						DescriptorCount = 1
					})
				});
			}
			disposables.Push(descriptorPool);

			// Allocate and update descriptor set
			VKDescriptorSet descriptorSet;
			{
				using MemoryStack sp = MemoryStack.Push();
				descriptorSet = descriptorPool.Allocate(new VKDescriptorSetAllocateInfo() {
					Type = VKStructureType.DescriptorSetAllocateInfo,
					DescriptorPool = descriptorPool,
					DescriptorSetCount = 1,
					SetLayouts = sp.Values(pipeline.DescriptorSetLayout)
				})[0];

				device.UpdateDescriptorSets(stackalloc VKWriteDescriptorSet[] {
					new() {
						Type = VKStructureType.WriteDescriptorSet,
						DescriptorCount = 1,
						DescriptorType = VKDescriptorType.CombinedImageSampler,
						DstArrayElement = 0,
						DstBinding = 0,
						DstSet = descriptorSet,
						ImageInfo = sp.Values(new VKDescriptorImageInfo() {
							ImageLayout = VKImageLayout.ShaderReadOnlyOptimal,
							ImageView = textureView,
							Sampler = sampler
						})
					}
				}, stackalloc VKCopyDescriptorSet[0]);
			}
			// Descriptor set will be freed with its associated pool

			// Create framebuffer
			FramebufferImpl framebuffer = new (device, physicalDevice, pipeline, swapchainImageFormat, swapchainImageExtent);
			// Again, don't push disposable because it has special handling

			Console.WriteLine("[Vulkan] Basic triangle - (Vertex Buffers, Textures, Pipelines, Render Passes, Descriptor Sets, Framebuffers)");

			while (!window.ShouldClose) {
				// Rewind semaphore manager
				sm.Rewind();

				// Acquire next image
				VKSemaphore semAcquireImage = sm.Next();
				VKResult resAcquireImage = swapchain.AcquireNextImage(ulong.MaxValue, semAcquireImage, null, out uint swapchainImageIndex);
				switch (resAcquireImage) {
					case VKResult.Success:
						break;
					case VKResult.SuboptimalKHR:
						if (ShouldRecreateSurface(physicalDevice, surface)) rebuildSwapchain = true;
						break;
					default:
						VK.CheckError(resAcquireImage);
						break;
				}

				// Get swapchain image and wait for availability
				SwapchainImage swapchainImage = swapchainImages[(int)swapchainImageIndex];
				swapchainImage.Fence.WaitFor(ulong.MaxValue);

				// Get command buffer
				var cmds = cm.Next();
				VKCommandBuffer cmdbuf = cmds.Item1;

				{
					using MemoryStack sp = MemoryStack.Push();

					// Record rendering commands
					RecordCommands(cmdbuf, vbo, descriptorSet, pipeline, framebuffer, swapchainImage);

					// Enqueue commands
					VKSemaphore semPresent = sm.Next();
					queue.Submit(new VKSubmitInfo() {
						Type = VKStructureType.SubmitInfo,
						CommandBufferCount = 1,
						CommandBuffers = sp.Values(cmdbuf.CommandBuffer),
						WaitSemaphoreCount = 1,
						WaitSemaphores = sp.Values(semAcquireImage),
						WaitDstStageMask = sp.Values(VKPipelineStageFlagBits.TopOfPipe),
						SignalSemaphoreCount = 1,
						SignalSemaphores = sp.Values(semPresent)
					}, cmds.Item2);

					// Enqueue present
					UnmanagedPointer<VKResult> pResult = sp.Alloc<VKResult>();
					queue.PresentKHR(new VKPresentInfoKHR() {
						Type = VKStructureType.PresentInfoKHR,
						SwapchainCount = 1,
						Swapchains = sp.Values(swapchain),
						ImageIndices = sp.Values(swapchainImageIndex),
						Results = pResult,
						WaitSemaphoreCount = 1,
						WaitSemaphores = sp.Values(semPresent)
					});

					// Determine if swapchain should be rebuild
					switch(pResult.Value) {
						case VKResult.Success:
							break;
						case VKResult.SuboptimalKHR:
							if (ShouldRecreateSurface(physicalDevice, surface)) rebuildSwapchain = true;
							break;
						case VKResult.ErrorOutOfDateKHR:
							rebuildSwapchain = true;
							break;
						default:
							VK.CheckError(pResult.Value);
							break;
					}
				}

				// Rebuild swapchain as necessary
				if (rebuildSwapchain) {
					device.WaitIdle();

					VKSwapchainKHR oldSwapchain = swapchain;
					swapchain = CreateSwapchain(physicalDevice, device, surface, window, out VKFormat newSwapchainImageFormat, out Vector2ui newSwapchainImageExtent, oldSwapchain);
					oldSwapchain.Dispose();

					if (newSwapchainImageExtent != framebuffer.Extent || newSwapchainImageFormat != framebuffer.ColorFormat) {
						framebuffer.Dispose();
						framebuffer = new(device, physicalDevice, pipeline, newSwapchainImageFormat, newSwapchainImageExtent);
					}

					foreach (SwapchainImage img in swapchainImages) img.Dispose();
					swapchainImages.Clear();
					foreach (VKImage img in swapchain.Images) swapchainImages.Add(new SwapchainImage(device, img, swapchainImageFormat));

					swapchainImageExtent = newSwapchainImageExtent;
					swapchainImageFormat = newSwapchainImageFormat;

					rebuildSwapchain = false;
				}

				GLFW3.PollEvents();
			}

			device.WaitIdle();

			framebuffer.Dispose();

			foreach (SwapchainImage img in swapchainImages) img.Dispose();
			swapchain.Dispose();

			while (disposables.Count > 0) disposables.Pop().Dispose();
		});

	}

}
