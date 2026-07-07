using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace MonsterHunt;

public sealed class Shader : IDisposable
{
    public int Handle { get; }

    public Shader(string vertexSrc, string fragmentSrc)
    {
        int vs = Compile(ShaderType.VertexShader, vertexSrc);
        int fs = Compile(ShaderType.FragmentShader, fragmentSrc);
        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vs);
        GL.AttachShader(Handle, fs);
        GL.LinkProgram(Handle);
        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new Exception("Erro ao linkar shader: " + GL.GetProgramInfoLog(Handle));
        GL.DetachShader(Handle, vs);
        GL.DeleteShader(vs);
        GL.DetachShader(Handle, fs);
        GL.DeleteShader(fs);
    }

    static int Compile(ShaderType type, string src)
    {
        int id = GL.CreateShader(type);
        GL.ShaderSource(id, src);
        GL.CompileShader(id);
        GL.GetShader(id, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) throw new Exception($"Erro ao compilar {type}: " + GL.GetShaderInfoLog(id));
        return id;
    }

    public void Use() => GL.UseProgram(Handle);

    public void Set(string name, Matrix4 m) =>
        GL.UniformMatrix4(GL.GetUniformLocation(Handle, name), true, ref m);

    public void Set(string name, Vector3 v) =>
        GL.Uniform3(GL.GetUniformLocation(Handle, name), v);

    public void Set(string name, Vector4 v) =>
        GL.Uniform4(GL.GetUniformLocation(Handle, name), v);

    public void Set(string name, float f) =>
        GL.Uniform1(GL.GetUniformLocation(Handle, name), f);

    public void Dispose() => GL.DeleteProgram(Handle);
}
