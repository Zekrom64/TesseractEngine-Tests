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
using Tesseract.Core.Resource;
using Tesseract.Core.Services;
using Tesseract.Core.Native;
using Tesseract.Core.Numerics;
using System.Numerics;

namespace Tesseract.Tests {

	public class ImGuiCoreDiagnosticState : ImGuiDiagnosticState, IDisposable {

		private readonly ITexture[] textures = new ITexture[16];
		private readonly nuint[] textureIDs = new nuint[16];

		public ImGuiCoreDiagnosticState(IGraphics graphics) {
			// Load the atlas texture
			AssemblyResourceDomain resourceDomain = new("core_imgui", typeof(ImGuiCoreDiagnosticState).Assembly) { PathPrefix = "Tesseract/Tests/Resources/" };
			var imageIO = new ImageSharpService();
			var atlasImage = imageIO.Load(new ResourceLocation(resourceDomain, "atlas.jpg"));
			if (atlasImage.Format != PixelFormat.R8G8B8A8UNorm) {
				var image2 = atlasImage.GetService(GraphicsServices.ProcessableImage)?.Convert(PixelFormat.R8G8B8A8UNorm) ?? throw new InvalidOperationException("Could not convert atlas to RGBA8 format");
				atlasImage.Dispose();
				atlasImage = image2;
			}

			// Create upload buffer
			using IBuffer transferBuffer = graphics.CreateBuffer(new BufferCreateInfo() {
				Size = (ulong)(atlasImage.Size.X * atlasImage.Size.Y * 4),
				Usage = BufferUsage.TransferSrc,
				MapFlags = MemoryMapFlags.Write
			});

			// Copy pixel data to upload buffer
			var pDst = transferBuffer.Map<byte>(MemoryMapFlags.Write);
			var pSrc = atlasImage.MapPixels(MapMode.ReadOnly);
			MemoryUtil.Copy(pDst, pSrc, transferBuffer.Size);

			// Unmap buffers
			atlasImage.UnmapPixels();
			transferBuffer.Unmap();

			// Create atlas texture
			using ITexture atlasTexture = graphics.CreateTexture(new TextureCreateInfo() {
				Size = (Vector3ui)(new Vector3i(atlasImage.Size, 1)),
				MipLevels = 1,
				ArrayLayers = 1,
				Format = PixelFormat.R8G8B8A8UNorm,
				Type = TextureType.Texture2D,
				Usage = TextureUsage.TransferDst | TextureUsage.TransferSrc
			});

			// Create command buffer
			using ICommandBuffer cmdBuffer = graphics.CreateCommandBuffer(new CommandBufferCreateInfo() { Type = CommandBufferType.Primary, Usage = CommandBufferUsage.Graphics | CommandBufferUsage.OneTimeSubmit });

			// Record transfer commands
			ICommandSink cmd = cmdBuffer.BeginRecording();
			cmd.Barrier(new ICommandSink.PipelineBarriers() {
				ProvokingStages = PipelineStage.Top,
				AwaitingStages = PipelineStage.Transfer,
				TextureMemoryBarriers = new ICommandSink.TextureMemoryBarrier[] {
					new ICommandSink.TextureMemoryBarrier() {
						Texture = atlasTexture,
						ProvokingAccess = default,
						AwaitingAccess = MemoryAccess.TransferWrite,
						OldLayout = TextureLayout.Undefined,
						NewLayout = TextureLayout.TransferDst,
						SubresourceRange = new TextureSubresourceRange() { Aspects = TextureAspect.Color, ArrayLayerCount = 1, MipLevelCount = 1 }
					}
				}
			});
			cmd.CopyBufferToTexture(atlasTexture, TextureLayout.TransferDst, transferBuffer, new ICommandSink.CopyBufferTexture() {
				TextureSize = atlasTexture.Size,
				TextureSubresource = new TextureSubresourceLayers() { Aspects = TextureAspect.Color, LayerCount = 1 }
			});
			cmd.Barrier(new ICommandSink.PipelineBarriers() {
				ProvokingStages = PipelineStage.Transfer,
				AwaitingStages = PipelineStage.Transfer,
				TextureMemoryBarriers = new ICommandSink.TextureMemoryBarrier[] {
					new ICommandSink.TextureMemoryBarrier() {
						Texture = atlasTexture,
						ProvokingAccess = MemoryAccess.TransferWrite,
						AwaitingAccess = MemoryAccess.TransferRead,
						OldLayout = TextureLayout.TransferDst,
						NewLayout = TextureLayout.TransferSrc,
						SubresourceRange = new TextureSubresourceRange() { Aspects = TextureAspect.Color, ArrayLayerCount = 1, MipLevelCount = 1 }
					}
				}
			});

			Vector2i textureSize = new Vector2i(atlasImage.Size) / 4;
			var textureCreateInfo = new TextureCreateInfo() {
				Size = (Vector3ui)(new Vector3i(textureSize, 1)),
				ArrayLayers = 1,
				MipLevels = 1,
				Format = PixelFormat.R8G8B8A8UNorm,
				Type = TextureType.Texture2D,
				Usage = TextureUsage.TransferDst | TextureUsage.Sampled
			};
			// Create each texture from the atlas
			for(int y = 0; y < 4; y++) {
				for(int x = 0; x < 4; x++) {
					int i = y * 4 + x;
					// Create individual texture
					ITexture texture = graphics.CreateTexture(textureCreateInfo);
					textures[i] = texture;
					// Copy to the atlas
					cmd.Barrier(new ICommandSink.PipelineBarriers() {
						ProvokingStages = PipelineStage.Top,
						AwaitingStages = PipelineStage.Transfer,
						TextureMemoryBarriers = new ICommandSink.TextureMemoryBarrier[] {
							new ICommandSink.TextureMemoryBarrier() {
								Texture = texture,
								ProvokingAccess = default,
								AwaitingAccess = MemoryAccess.TransferWrite,
								OldLayout = TextureLayout.Undefined,
								NewLayout = TextureLayout.TransferDst,
								SubresourceRange = new TextureSubresourceRange() { Aspects = TextureAspect.Color, ArrayLayerCount = 1, MipLevelCount = 1 }
							}
						}
					});
					cmd.CopyTexture(texture, TextureLayout.TransferDst, atlasTexture, TextureLayout.TransferSrc, new ICommandSink.CopyTextureRegion() {
						Aspect = TextureAspect.Color,
						Size = (Vector3ui)(new Vector3i(textureSize, 1)),
						SrcOffset = (Vector3ui)(new Vector3i(x * textureSize.X, y * textureSize.Y, 0)),
						SrcSubresource = new TextureSubresourceLayers() { Aspects = TextureAspect.Color, LayerCount = 1 },
						DstSubresource = new TextureSubresourceLayers() { Aspects = TextureAspect.Color, LayerCount = 1 }
					});
					// Generate usage barrier
					cmd.Barrier(new ICommandSink.PipelineBarriers() {
						ProvokingStages = PipelineStage.Transfer,
						AwaitingStages = PipelineStage.FragmentShader,
						TextureMemoryBarriers = new ICommandSink.TextureMemoryBarrier[] {
							new ICommandSink.TextureMemoryBarrier() {
								Texture = texture,
								ProvokingAccess = MemoryAccess.TransferWrite,
								AwaitingAccess = MemoryAccess.ShaderRead,
								OldLayout = TextureLayout.TransferDst,
								NewLayout = TextureLayout.ShaderSampled,
								SubresourceRange = new TextureSubresourceRange() { Aspects = TextureAspect.Color, ArrayLayerCount = 1, MipLevelCount = 1 }
							}
						}
					});
				}
			}

			// End recording
			cmdBuffer.EndRecording();

			// Create fence
			using ISync fence = graphics.CreateSync(new SyncCreateInfo() { Direction = SyncDirection.GPUToHost, Features = SyncFeatures.GPUWorkSignaling | SyncFeatures.HostWaiting, Granularity = SyncGranularity.CommandBuffer });

			// Submit and await commands
			graphics.SubmitCommands(new IGraphics.CommandBufferSubmitInfo() {
				CommandBuffer = new ICommandBuffer[] { cmdBuffer },
				SignalSync = new ISync[] { fence }
			});
			fence.HostWait(ulong.MaxValue);

			// Map the textures to get IDs
			for(int i = 0; i < 16; i++) textureIDs[i] = ImGuiCoreRender.MapTexture(textures[i]);

			atlasImage.Dispose();
		}

		private int imageCount = 16;

		public override void Render() {
			base.Render();

			var im = GImGui.Instance;

			im.Begin("Load Generator"u8);
			im.DragInt("Image Count"u8, ref imageCount, vMin: 0, vMax: 1024);

			Random random = new(0);

			for(int i = 0; i < imageCount; i++) {
				if ((i % 16) != 0) im.SameLine();
				im.Image(textureIDs[random.Next(16)], new Vector2(16, 16));
			}

			im.End();
		}

		public void Dispose() {
			GC.SuppressFinalize(this);
			foreach(ITexture texture in textures) texture.Dispose();
		}

	}

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

			ImGuiCoreDiagnosticState state = new(graphics);

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

			state.Dispose();

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
