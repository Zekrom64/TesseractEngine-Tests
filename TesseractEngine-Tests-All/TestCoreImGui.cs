using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract.Core.Graphics.Accelerated;
using Tesseract.Core.Graphics;
using Tesseract.Core.Input;
using Tesseract.OpenGL.Graphics;
using Tesseract.OpenGL;
using Tesseract.GLFW.Services;
using Tesseract.GLFW;
using Tesseract.Vulkan.Services.Objects;
using Tesseract.Vulkan.Services;
using System.Threading;
using Tesseract.CLI.ImGui;
using Tesseract.ImGui.Core;
using Tesseract.ImGui;
using System.Diagnostics;

namespace Tesseract.Tests {

	public static class TestCoreImGui {

		private static void TestImGuiImpl(IGraphicsEnumerator enumerator, IInputSystem inputSystem, IWindow window, IWindowSystem windowSystem) {
			GImGui.Instance = new ImGuiCLI();
			GImGui.CurrentContext = GImGui.CreateContext();

			var provider = enumerator.EnumerateProviders().First();
			using IGraphics graphics = provider.CreateGraphics(new GraphicsCreateInfo() { });

			// Create swapchain and get the array of images
			using ISwapchain swapchain = provider.CreateSwapchain(graphics, new SwapchainCreateInfo() {
				ImageUsage = TextureUsage.TransferDst | TextureUsage.ColorAttachment,
				PresentMode = SwapchainPresentMode.FIFO,
				PresentWindow = window
			});

			ImGuiCoreRender.Init(graphics, new ImGuiCoreRenderInfo() {
				InitialLayout = TextureLayout.Undefined,
				FinalLayout = TextureLayout.PresentSrc,
				FramebufferFormat = swapchain.Format,
				PreserveFramebuffer = false
			});

			var swapchainImages = swapchain.Images;
			ITextureView[] swapchainTextureViews = new ITextureView[swapchainImages.Length];
			IFramebuffer[] framebuffers = new IFramebuffer[swapchainImages.Length];
			bool disposeSwapchainResources = false;

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
					RenderPass = ImGuiCoreRender.RenderPass,
					Size = swapchain.Size
				};
				for (int i = 0; i < swapchainImages.Length; i++) framebuffers[i] = graphics.CreateFramebuffer(framebufferInfo with { Attachments = new ITextureView[] { swapchainTextureViews[i] } });

				disposeSwapchainResources = true;
			}

			ImGuiCoreInput.Init(inputSystem, window, windowSystem);

			// Create semaphores
			var semaphoreCreateInfo = new SyncCreateInfo() { Direction = SyncDirection.GPUToGPU, Features = SyncFeatures.GPUWorkSignaling | SyncFeatures.GPUWorkWaiting, Granularity = SyncGranularity.CommandBuffer };
			using ISync semaphoreImage = graphics.CreateSync(semaphoreCreateInfo); // Swapchain Image Ready -> Command Submission
			using ISync semaphoreReady = graphics.CreateSync(semaphoreCreateInfo); // Command Submission -> Swapchain Present

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

			ImGuiDiagnosticState state = new();

			while (!window.Closing) {
				// Get the next image from the swaphchain
				int imageIndex = swapchain.BeginFrame(semaphoreImage);
				var framebuffer = framebuffers[imageIndex];

				ImGuiCoreInput.NewFrame();
				ImGuiCoreRender.NewFrame();
				GImGui.NewFrame();

				state.Render();

				GImGui.Render();

				state.MarkBeginRendering();
				ImGuiCoreRender.RenderDrawData(GImGui.GetDrawData(), framebuffer, submitInfo);
				graphics.WaitIdle();
				state.MarkEndRendering();

				// Submit the frame for presentation
				swapchain.EndFrame(null, semaphoreReady);

				// Run events and sleep
				inputSystem.RunEvents();
				Thread.Sleep(10);
			}

			graphics.WaitIdle();

			commandBuffer?.Dispose();

			if (disposeSwapchainResources) {
				foreach (IFramebuffer framebuffer in framebuffers) framebuffer.Dispose();
				foreach (ITextureView texView in swapchainTextureViews) texView.Dispose();
			}

			ImGuiCoreRender.Shutdown();
			GImGui.DestroyContext();
		}

		private static void TestImGuiVulkan() {
			GLFWVKServices.Register();

			IInputSystem inputSystem = new GLFWServiceInputSystem();
			IWindowSystem windowSystem = new GLFWServiceWindowSystem();
			using IWindow window = windowSystem.CreateWindow("Test", 800, 600, new WindowAttributeList() {
				{ WindowAttributes.Resizable, false },
				{ VulkanWindowAttributes.VulkanWindow, true }
			});

			using IGraphicsEnumerator graphicsEnumerator = VulkanGraphicsEnumerator.GetEnumerator(
				new GraphicsEnumeratorCreateInfo() {
#if DEBUG
					EnableDebugExtensions = true,
#endif
					Window = window
				}
			);

			TestImGuiImpl(graphicsEnumerator, inputSystem, window, windowSystem);
		}

		private static void TestImGuiGL() {
			GLFWGLServices.Register();

			IInputSystem inputSystem = new GLFWServiceInputSystem();
			IWindowSystem windowSystem = new GLFWServiceWindowSystem();
			using IWindow window = windowSystem.CreateWindow("Test", 800, 600, new WindowAttributeList() {
				{ WindowAttributes.Resizable, false },
				{ GLWindowAttributes.OpenGLWindow, true },
#if DEBUG
				{ GLWindowAttributes.DebugContext, true },
#else
				{ GLWindowAttributes.NoError, true },
#endif
				{ GLWindowAttributes.ContextVersionMajor, 4 },
				{ GLWindowAttributes.ContextVersionMinor, 5 },
				{ GLWindowAttributes.ContextProfile, GLProfile.Core }
			});

			using IGraphicsEnumerator graphicsEnumerator = GLGraphicsEnumerator.GetEnumerator(
				new GraphicsEnumeratorCreateInfo() {
#if DEBUG
					EnableDebugExtensions = true,
#endif
					Window = window
				}
			);

			TestImGuiImpl(graphicsEnumerator, inputSystem, window, windowSystem);
		}

		public static void TestCoreImGuiSDLVulkan() => TestGLFW.RunWithGLFW(TestImGuiVulkan);

		public static void TestCoreImGuiSDLGL() => TestGLFW.RunWithGLFW(TestImGuiGL);

	}

}
