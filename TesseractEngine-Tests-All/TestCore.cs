using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tesseract.Core.Collections;
using Tesseract.Core.Graphics;
using Tesseract.Core.Graphics.Accelerated;
using Tesseract.Core.Input;
using Tesseract.Core.Native;
using Tesseract.Core.Numerics;
using Tesseract.GLFW;
using Tesseract.GLFW.Services;
using Tesseract.OpenGL;
using Tesseract.OpenGL.Graphics;
using Tesseract.Vulkan;
using Tesseract.Vulkan.Services;
using Tesseract.Vulkan.Services.Objects;

namespace Tesseract.Tests {

	public static class TestCore {

		public static void TestCoreGraphicsVulkan() => TestGLFW.RunWithGLFW(TestVulkan);

		public static void TestCoreGraphicsGL() => TestGLFW.RunWithGLFW(TestGL);

		// Generic draw implementation
		private static void TestDraw(IFramebuffer framebuffer, IRenderPass renderPass, ICommandSink cmd) {
			cmd.BeginRenderPass(new ICommandSink.RenderPassBegin() {
				Framebuffer = framebuffer,
				RenderPass = renderPass,
				RenderArea = new Recti(framebuffer.Size),
				ClearValues = new ICommandSink.ClearValue[] {
					new ICommandSink.ClearValue() {
						Aspect = TextureAspect.Color,
						Color= new Vector4(1, 0, 0, 1)
					}
				}
			}, SubpassContents.Inline);

			cmd.EndRenderPass();
		}

		// Generic test implementation
		private static void TestImpl(IGraphicsEnumerator enumerator, IInputSystem inputSystem, IWindow window) {
			var graphicsProvider = enumerator.EnumerateProviders().First();

			using IGraphics graphics = graphicsProvider.CreateGraphics(new GraphicsCreateInfo() { });

			// Create swapchain and get the array of images
			using ISwapchain swapchain = graphicsProvider.CreateSwapchain(graphics, new SwapchainCreateInfo() {
				ImageUsage = TextureUsage.TransferDst | TextureUsage.ColorAttachment,
				PresentMode = SwapchainPresentMode.FIFO,
				PresentWindow = window
			});
			var swapchainImages = swapchain.Images;
			ITextureView[] swapchainTextureViews = new ITextureView[swapchainImages.Length];
			IFramebuffer[] framebuffers = new IFramebuffer[swapchainImages.Length];
			bool disposeSwapchainResources = false;

			// Create render pass
			RenderPassCreateInfo renderPassInfo = new() {
				Attachments = new RenderPassAttachment[] {
					new RenderPassAttachment() {
						InitialLayout = TextureLayout.Undefined,
						FinalLayout = TextureLayout.PresentSrc,
						Format = swapchain.Format,
						Samples = 1,
						LoadOp = AttachmentLoadOp.Clear,
						StoreOp = AttachmentStoreOp.Store,
						StencilLoadOp = AttachmentLoadOp.DontCare,
						StencilStoreOp = AttachmentStoreOp.DontCare
					}
				},
				Subpasses = new RenderPassSubpass[] {
					new RenderPassSubpass() {
						ColorAttachments = new RenderPassAttachmentReference[] {
							new RenderPassAttachmentReference() {
								Attachment = 0,
								Layout = TextureLayout.ColorAttachment
							}
						}
					}
				}
			};
			using IRenderPass renderPass = graphics.CreateRenderPass(renderPassInfo);
			
			if (swapchain.ImageType == SwapchainImageType.Framebuffer) framebuffers = swapchainImages.Cast<IFramebuffer>().ToArray();
			else {
				// Create swapchain texture views
				TextureViewCreateInfo texViewInfo = new() {
					Format = swapchain.Format,
					Mapping = default,
					SubresourceRange = new TextureSubresourceRange() {
						Aspects = TextureAspect.Color,
						ArrayLayerCount = 1,
						MipLevelCount = 1,
						BaseArrayLayer = 0,
						BaseMipLevel = 0
					},
					Type = TextureType.Texture2D,
					Texture = null!
				};
				for (int i = 0; i < swapchainImages.Length; i++) swapchainTextureViews[i] = graphics.CreateTextureView(texViewInfo with { Texture = (ITexture)swapchainImages[i] });

				// Create swapchain framebuffers
				FramebufferCreateInfo framebufferInfo = new() {
					Layers = 1,
					Attachments = null!,
					RenderPass = renderPass,
					Size = swapchain.Size
				};
				for (int i = 0; i < swapchainImages.Length; i++) framebuffers[i] = graphics.CreateFramebuffer(framebufferInfo with { Attachments = new ITextureView[] { swapchainTextureViews[i] } });

				disposeSwapchainResources = true;
			}

			// Create semaphores
			var semaphoreCreateInfo = new SyncCreateInfo() { Direction = SyncDirection.GPUToGPU, Features = SyncFeatures.GPUWorkSignaling | SyncFeatures.GPUWorkWaiting, Granularity = SyncGranularity.CommandBuffer };
			using ISync semaphoreImage = graphics.CreateSync(semaphoreCreateInfo); // Swapchain Image Ready -> Command Submission
			using ISync semaphoreReady = graphics.CreateSync(semaphoreCreateInfo); // Command Submission -> Swapchain Present
			// Create fence
			var fenceCreateInfo = new SyncCreateInfo() { Direction = SyncDirection.GPUToHost, Features = SyncFeatures.GPUWorkSignaling | SyncFeatures.HostWaiting, Granularity = SyncGranularity.CommandBuffer };
			ISync? fenceCompleted = null; // Signaled when commands finish execution GPU-side (cannot reset the buffer until then!)

			// Allocate command buffer and containing array
			ICommandBuffer? commandBuffer = null;
			ICommandBuffer[] commandBuffers = Array.Empty<ICommandBuffer>();
			if (graphics.Properties.PreferredCommandMode == CommandMode.Buffered) {
				commandBuffer = graphics.CreateCommandBuffer(new CommandBufferCreateInfo() { Type = CommandBufferType.Primary, Usage = CommandBufferUsage.Graphics | CommandBufferUsage.Rerecordable });
				commandBuffers = new ICommandBuffer[] { commandBuffer };
			}

			// Create command submission information
			IGraphics.CommandBufferSubmitInfo submitInfo = new() {
				WaitSync = new (ISync, PipelineStage)[] { (semaphoreImage, PipelineStage.Top) },
				SignalSync = new ISync[] { semaphoreReady }
			};

			while (!window.Closing) {
				// Get the next image from the swaphchain
				int imageIndex = swapchain.BeginFrame(semaphoreImage);
				var framebuffer = framebuffers[imageIndex];

				// If recording to a buffer
				if (commandBuffer != null) {
					// Wait until the commands have completed before we start reusing the buffer
					if (fenceCompleted != null) {
						fenceCompleted.HostWait(ulong.MaxValue);
						fenceCompleted.HostReset();
					}

					// Record commands into the buffer
					var cmd = commandBuffer.BeginRecording();
					TestDraw(framebuffer, renderPass, cmd);
					commandBuffer.EndRecording();

					// Create the completion fence if it doesn't already exist
					if (fenceCompleted == null) {
						fenceCompleted = graphics.CreateSync(fenceCreateInfo);
						submitInfo = submitInfo with { SignalSync = new ISync[] { semaphoreReady, fenceCompleted } };
					}

					// Submit the commands to the GPU
					graphics.SubmitCommands(submitInfo with { CommandBuffer = commandBuffers });
				} else {
					// Else just run the commands directly
					graphics.RunCommands(cmd => TestDraw(framebuffer, renderPass, cmd), CommandBufferUsage.Graphics, submitInfo);
				}

				// Submit the frame for presentation
				swapchain.EndFrame(null, semaphoreReady);

				// Run events and sleep
				inputSystem.RunEvents();
				Thread.Sleep(10);
			}

			graphics.WaitIdle();

			fenceCompleted?.Dispose();
			commandBuffer?.Dispose();

			if (disposeSwapchainResources) {
				foreach (IFramebuffer framebuffer in framebuffers) framebuffer.Dispose();
				foreach (ITextureView texView in swapchainTextureViews) texView.Dispose();
			}
		}

		private static void TestVulkan() {
			// Service registration
			GLFWVKServices.Register();

			// Window system creation
			IInputSystem inputSystem = new GLFWServiceInputSystem();
			IWindowSystem windowSystem = new GLFWServiceWindowSystem();
			using IWindow window = windowSystem.CreateWindow("Test", 800, 600, new WindowAttributeList() {
				{ WindowAttributes.Resizable, false },
				{ VulkanWindowAttributes.VulkanWindow, true }
			});

			// Context creation
			using IGraphicsEnumerator graphicsEnumerator = VulkanGraphicsEnumerator.GetEnumerator(
				new GraphicsEnumeratorCreateInfo() {
					EnableDebugExtensions = true,
					Window = window,
					ExtendedInfo = new IExtendedGraphicsEnumeratorInfo[] {
						new VulkanExtendedGraphicsEnumeratorInfo() {
							ApplicationName = "Test",
							ApplicationVersion = VK10.MakeApiVersion(0, 1, 0, 0),
							EngineName = "Test",
							EngineVersion = VK10.MakeApiVersion(0, 1, 0, 0)
						}
					}
				}
			);

			TestImpl(graphicsEnumerator, inputSystem, window);
		}

		private static void TestGL() {
			// Service registration
			GLFWGLServices.Register();

			// Window system creation
			IInputSystem inputSystem = new GLFWServiceInputSystem();
			IWindowSystem windowSystem = new GLFWServiceWindowSystem();
			using IWindow window = windowSystem.CreateWindow("Test", 800, 600, new WindowAttributeList() {
				{ WindowAttributes.Resizable, false },
				{ GLWindowAttributes.OpenGLWindow, true },
				{ GLWindowAttributes.DebugContext, true },
				{ GLWindowAttributes.ContextVersionMajor, 3 },
				{ GLWindowAttributes.ContextVersionMinor, 3 },
				{ GLWindowAttributes.ContextProfile, GLProfile.Core }
			});

			// Context creation
			using IGraphicsEnumerator graphicsEnumerator = GLGraphicsEnumerator.GetEnumerator(
				new GraphicsEnumeratorCreateInfo() {
					EnableDebugExtensions = true,
					Window = window
				}
			);

			TestImpl(graphicsEnumerator, inputSystem, window);
		}

	}

}
