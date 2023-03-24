using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Tesseract.Core.Numerics;
using Tesseract.Core.Native;
using Tesseract.GLFW;
using Tesseract.GLFW.Services;
using Tesseract.OpenGL;
using Tesseract.OpenGL.Native;

namespace Tesseract.Tests {
	
	public static class TestGL {

		public const string VertexShader = @"
#version 330

layout(location = 0)
in vec3 inPosition;
layout(location = 1)
in vec3 inColor;
layout(location = 2)
in vec2 inTexCoord;

out vec2 fragTexCoord;
out vec3 fragColor;

void main() {
	gl_Position = vec4(inPosition, 1.0);
	fragTexCoord = inTexCoord;
	fragColor = inColor;
}
		";

		public const string FragmentShader = @"
#version 330

in vec2 fragTexCoord;
in vec3 fragColor;

layout(location = 0)
out vec4 outColor0;

uniform sampler2D uTexture;

void main() {
	outColor0 = texture2D(uTexture, fragTexCoord) * vec4(fragColor, 1.0);
}
		";

		public struct Vertex {

			public Vector3 Position;

			public Vector3 Color;

			public Vector2 TexCoord;

		}

		public static void TestRaw() => TestGLFW.RunWithGLFW(() => {
			GLFW3.DefaultWindowHints();
			GLFW3.WindowHint(GLFWWindowAttrib.Resizable, 0);
			GLFW3.WindowHint(GLFWWindowAttrib.OpenGLDebugContext, 1);
			GLFWGLServices.Register();
			GLFWWindow window = new(new Vector2i(800, 600), "GL Test");

			GLFWGLContext glcontext = new(window);
			glcontext.MakeGLCurrent();
			GL gl = new(glcontext);

			if (gl.KHRDebug != null) {
				gl.KHRDebug.DebugMessageCallback((GLDebugSource source, GLDebugType type, uint id, GLDebugSeverity severity, int length, string message, IntPtr userParam) => {
					if (type == GLDebugType.Error) throw new GLException("OpenGL Error: " + message);
				}, IntPtr.Zero);
			}

			Console.WriteLine($"[OpenGL] OpenGL Context {gl.Context.MajorVersion}.{gl.Context.MinorVersion}");

			GL33? gl33 = gl.GL33;
			if (gl33 == null) throw new GLException("Cannot test OpenGL with version <3.3");

			Console.WriteLine("[OpenGL] Basic triangle - (Vertex Buffers/Arrays, Textures, Shader Programs)");

			// Set color clear value
			gl33.ColorClearValue = new Vector4(0, 0, 0, 1);
			
			// Create vertex buffer
			uint vbo = gl33.GenBuffers();
			gl33.BindBuffer(GLBufferTarget.Array, vbo);
			gl33.BufferData(GLBufferTarget.Array, GLBufferUsage.StaticDraw,
				new Vertex() { Position = new(-1, -1, 0), Color = new(1, 0, 0), TexCoord = new(0, 0) },
				new Vertex() { Position = new( 0,  1, 0), Color = new(0, 1, 0), TexCoord = new(0.5f, 1) },
				new Vertex() { Position = new( 1, -1, 0), Color = new(0, 0, 1), TexCoord = new(1, 0) }
			);

			// Create vertex array
			uint vao = gl33.GenVertexArrays();
			gl33.BindVertexArray(vao);
			int stride = Marshal.SizeOf<Vertex>();
			gl33.EnableVertexAttribArray(0);
			gl33.VertexAttribPointer(0, 3, GLTextureType.Float, false, stride, Marshal.OffsetOf<Vertex>("Position"));
			gl33.EnableVertexAttribArray(1);
			gl33.VertexAttribPointer(1, 3, GLTextureType.Float, false, stride, Marshal.OffsetOf<Vertex>("Color"));
			gl33.EnableVertexAttribArray(2);
			gl33.VertexAttribPointer(2, 2, GLTextureType.Float, false, stride, Marshal.OffsetOf<Vertex>("TexCoord"));

			// Create shader modules
			uint vs = gl33.CreateShader(GLShaderType.Vertex);
			gl33.ShaderSource(vs, VertexShader);
			gl33.CompileShader(vs);
			if (gl33.GetShader(vs, GLGetShader.CompileStatus) == 0) throw new GLException("Failed to compile vertex shader: \n" + gl33.GetShaderInfoLog(vs));
			uint fs = gl33.CreateShader(GLShaderType.Fragment);
			gl33.ShaderSource(fs, FragmentShader);
			gl33.CompileShader(fs);
			if (gl33.GetShader(fs, GLGetShader.CompileStatus) == 0) throw new GLException("Failed to compile fragment shader: \n" + gl33.GetShaderInfoLog(fs));
			  
			// Create shader program
			uint shaderProgram = gl33.CreateProgram();
			gl33.AttachShader(shaderProgram, vs);
			gl33.AttachShader(shaderProgram, fs);
			gl33.LinkProgram(shaderProgram);
			if (gl33.GetProgram(shaderProgram, GLGetProgram.LinkStatus) == 0) throw new GLException("Failed to link shader program: \n" + gl33.GetProgramInfoLog(shaderProgram));
			gl33.UseProgram(shaderProgram);
			gl33.Uniform(gl33.GetUniformLocation(shaderProgram, "uTexture"), 0);

			// Create texture
			uint[] texturePixels = new uint[32 * 32];
			bool pixelState = false;
			for(int i = 0; i < texturePixels.Length; i++) {
				if ((i % 32) == 0) pixelState = !pixelState;
				texturePixels[i] = pixelState ? 0xFFFFFFFF : 0xFFCFCFCF;
				pixelState = !pixelState;
			}
			uint texture = gl33.GenTextures();
			gl33.ActiveTexture = 0;
			gl33.BindTexture(GLTextureTarget.Texture2D, texture);
			gl33.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.RGBA, 32, 32, 0, GLFormat.RGBA, GLTextureType.UnsignedByte, texturePixels);
			gl33.GenerateMipmap(GLTextureTarget.Texture2D);

			// Create sampler
			uint sampler = gl33.GenSamplers();
			gl33.SamplerParameter(sampler, GLSamplerParameter.MinFilter, GLEnums.GL_NEAREST_MIPMAP_NEAREST);
			gl33.SamplerParameter(sampler, GLSamplerParameter.MagFilter, (int)GLFilter.Nearest);
			gl33.BindSampler(0, sampler);


			while (!window.ShouldClose) {
				gl33.Clear(GLBufferMask.Color);
				gl33.DrawArrays(GLDrawMode.Triangles, 0, 3);

				window.SwapBuffers();
				GLFW3.WaitEvents(0.1);
			}

			gl33.DeleteSamplers(sampler);
			gl33.DeleteTextures(texture);

			gl33.DeleteProgram(shaderProgram);
			gl33.DeleteShader(fs);
			gl33.DeleteShader(vs);

			gl33.DeleteVertexArrays(vao);
			gl33.DeleteBuffers(vbo);

			window.Dispose();
		});

	}
}
