using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[System.Serializable]
public class ReCurDenoiserGeneralized {

    private ComputeShader RecurrentDenoiser;
    
    //Main accumulation/blur textures
        private RenderTexture HFA;
        private RenderTexture HFB;
        private RenderTexture HFPrev;
    
    //SSAO textures
        private RenderTexture SSAOTexA;
        private RenderTexture SSAOTexB;

    //Long Accumulation Textures(Second temporal pass, dont get recursively blurred)
        private RenderTexture HFLAA;
        private RenderTexture HFLAB;

    //Screenspace geometric information
        private RenderTexture NormA;
        private RenderTexture NormB;
        private RenderTexture PrevDepth;

    //Stores roughness info to guide blur
        private RenderTexture BlurHints;

    private int DataPackKernel;
    private int BlurKernel;
    private int FastTemporalKernel;
    private int SlowTemporalKernel;
    private int SSAOKernel;
    private int SSAOFilterKernel;

    private Camera camera;
    private int ScreenWidth;
    private int ScreenHeight;
    private float BlurRadius;
    private int CurFrame;

    private void CreateRenderTexture(ref RenderTexture ThisTex, RenderTextureFormat RenderFormat) {
        ThisTex = new RenderTexture(ScreenWidth, ScreenHeight, 0,
            RenderFormat, RenderTextureReadWrite.Linear);
        ThisTex.enableRandomWrite = true;
        ThisTex.useMipMap = false;
        ThisTex.Create();
    }

    public void Clear() {
        SSAOTexA?.Release();
        SSAOTexB?.Release();
        PrevDepth?.Release();
        BlurHints?.Release();
        HFA?.Release();
        HFB?.Release();
        HFPrev?.Release();
        HFLAA?.Release();
        HFLAB?.Release();
        NormA?.Release();
        NormB?.Release();
    }

    public ReCurDenoiserGeneralized(int ScreenWidth, int ScreenHeight, float BlurRadius, Camera camera, ComputeShader shader) {
        this.ScreenWidth = ScreenWidth;
        this.ScreenHeight = ScreenHeight;
        this.camera = camera;
        this.BlurRadius = BlurRadius;
        this.RecurrentDenoiser = shader;
        CurFrame = 0;

        DataPackKernel = RecurrentDenoiser.FindKernel("DataPackKernel");
        BlurKernel = RecurrentDenoiser.FindKernel("BlurKernel");
        FastTemporalKernel = RecurrentDenoiser.FindKernel("FastTemporalKernel");
        SlowTemporalKernel = RecurrentDenoiser.FindKernel("SlowTemporalKernel");
        SSAOKernel = RecurrentDenoiser.FindKernel("SSAO");
        SSAOFilterKernel = RecurrentDenoiser.FindKernel("SSAOFilter");

        CreateRenderTexture(ref SSAOTexA, RenderTextureFormat.RHalf);
        CreateRenderTexture(ref SSAOTexB, RenderTextureFormat.RHalf);
        CreateRenderTexture(ref PrevDepth, RenderTextureFormat.RHalf);
        CreateRenderTexture(ref BlurHints, RenderTextureFormat.RHalf);

        CreateRenderTexture(ref HFA, RenderTextureFormat.ARGBHalf);
        CreateRenderTexture(ref HFB, RenderTextureFormat.ARGBHalf);
        CreateRenderTexture(ref HFPrev, RenderTextureFormat.ARGBHalf);
        CreateRenderTexture(ref HFLAA, RenderTextureFormat.ARGBHalf);
        CreateRenderTexture(ref HFLAB, RenderTextureFormat.ARGBHalf);
        
        CreateRenderTexture(ref NormA, RenderTextureFormat.RGFloat);
        CreateRenderTexture(ref NormB, RenderTextureFormat.RGFloat);

        //Non frame varying information
        RecurrentDenoiser.SetInt("screen_width", ScreenWidth);
        RecurrentDenoiser.SetInt("screen_height", ScreenHeight);
        RecurrentDenoiser.SetFloat("gBlurRadius", BlurRadius);
        RecurrentDenoiser.SetTexture(DataPackKernel, "BlurHintsWrite", BlurHints);
        RecurrentDenoiser.SetTexture(BlurKernel, "BlurHints", BlurHints);
        RecurrentDenoiser.SetTexture(FastTemporalKernel, "PrevDepth", PrevDepth);
        RecurrentDenoiser.SetTexture(SlowTemporalKernel, "PrevDepth", PrevDepth);
        RecurrentDenoiser.SetTexture(SlowTemporalKernel, "HFB", HFB);
        RecurrentDenoiser.SetTexture(SSAOKernel, "SSAOWrite", SSAOTexA);
        RecurrentDenoiser.SetTexture(SSAOFilterKernel, "SSAORead", SSAOTexA);
        RecurrentDenoiser.SetTexture(SSAOFilterKernel, "SSAOWrite", SSAOTexB);
        RecurrentDenoiser.SetTexture(BlurKernel, "SSAORead", SSAOTexB);
    }

    public void Denoise(CommandBuffer cmd, 
                        ref RenderTexture destination, 
                        RenderTexture RadianceTex, 
                        RenderTexture Albedo, 
                        RenderTexture Depth, 
                        RenderTexture GeomNorm, 
                        RenderTexture SurfNorm, 
                        RenderTexture MetRough) 
    {
        CurFrame++;
        bool EvenFrame = CurFrame % 2 == 0;//used for texture pingponging
        RecurrentDenoiser.SetMatrix("CameraToWorld", camera.cameraToWorldMatrix);
        RecurrentDenoiser.SetMatrix("CamInvProj", camera.projectionMatrix.inverse);
        RecurrentDenoiser.SetMatrix("ViewProj", camera.projectionMatrix * camera.worldToCameraMatrix);
        RecurrentDenoiser.SetTextureFromGlobal(FastTemporalKernel, "MotionVectors", "_CameraMotionVectorsTexture");
        RecurrentDenoiser.SetTextureFromGlobal(SlowTemporalKernel, "MotionVectors", "_CameraMotionVectorsTexture");

        cmd.SetComputeTextureParam(RecurrentDenoiser, DataPackKernel, "Albedo", Albedo);
        cmd.SetComputeTextureParam(RecurrentDenoiser, DataPackKernel, "SurfaceNormal", SurfNorm);
        cmd.SetComputeTextureParam(RecurrentDenoiser, DataPackKernel, "GeometricNormal", GeomNorm);
        cmd.SetComputeTextureParam(RecurrentDenoiser, DataPackKernel, "MetallicRoughness", MetRough);
        cmd.SetComputeTextureParam(RecurrentDenoiser, DataPackKernel, "IncommingIrradiance", RadianceTex);
        cmd.SetComputeTextureParam(RecurrentDenoiser, DataPackKernel, "HFA", EvenFrame ? HFA : HFPrev);
        cmd.SetComputeTextureParam(RecurrentDenoiser, DataPackKernel, "NormA", EvenFrame ? NormA : NormB);
        cmd.DispatchCompute(RecurrentDenoiser, DataPackKernel, Mathf.CeilToInt(ScreenWidth / 32.0f), Mathf.CeilToInt(ScreenHeight / 32.0f), 1);

        cmd.SetComputeTextureParam(RecurrentDenoiser, SSAOKernel, "CurDepth", Depth);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SSAOKernel, "NormB", EvenFrame ? NormA : NormB);
        cmd.DispatchCompute(RecurrentDenoiser, SSAOKernel, Mathf.CeilToInt(ScreenWidth / 16.0f), Mathf.CeilToInt(ScreenHeight / 16.0f), 1);

        cmd.SetComputeTextureParam(RecurrentDenoiser, SSAOFilterKernel, "CurDepth", Depth);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SSAOFilterKernel, "NormB", EvenFrame ? NormA : NormB);
        cmd.DispatchCompute(RecurrentDenoiser, SSAOFilterKernel, Mathf.CeilToInt(ScreenWidth / 8.0f), Mathf.CeilToInt(ScreenHeight / 8.0f), 1);


        cmd.SetComputeTextureParam(RecurrentDenoiser, FastTemporalKernel, "CurDepth", Depth);
        cmd.SetComputeTextureParam(RecurrentDenoiser, FastTemporalKernel, "HFA", EvenFrame ? HFA : HFPrev);
        cmd.SetComputeTextureParam(RecurrentDenoiser, FastTemporalKernel, "HFPrev", EvenFrame ? HFPrev : HFA);
        cmd.SetComputeTextureParam(RecurrentDenoiser, FastTemporalKernel, "NormA", EvenFrame ? NormA : NormB);
        cmd.SetComputeTextureParam(RecurrentDenoiser, FastTemporalKernel, "NormB", EvenFrame ? NormB : NormA);
        cmd.DispatchCompute(RecurrentDenoiser, FastTemporalKernel, Mathf.CeilToInt(ScreenWidth / 8.0f), Mathf.CeilToInt(ScreenHeight / 8.0f), 1);

        cmd.Blit(EvenFrame ? HFA : HFPrev, EvenFrame ? HFPrev : HFA);
        

        cmd.SetComputeIntParam(RecurrentDenoiser, "PassNum", 2);
        cmd.SetComputeTextureParam(RecurrentDenoiser, BlurKernel, "CurDepth", Depth);
        cmd.SetComputeTextureParam(RecurrentDenoiser, BlurKernel, "HFA", EvenFrame ? HFA : HFPrev);
        cmd.SetComputeTextureParam(RecurrentDenoiser, BlurKernel, "HFB", EvenFrame ? HFPrev : HFA);
        cmd.SetComputeTextureParam(RecurrentDenoiser, BlurKernel, "NormB", EvenFrame ? NormA : NormB);
        cmd.DispatchCompute(RecurrentDenoiser, BlurKernel, Mathf.CeilToInt(ScreenWidth / 16.0f), Mathf.CeilToInt(ScreenHeight / 16.0f), 1);

        cmd.SetComputeIntParam(RecurrentDenoiser, "PassNum", 3);
        cmd.SetComputeTextureParam(RecurrentDenoiser, BlurKernel, "HFA", HFB);
        cmd.SetComputeTextureParam(RecurrentDenoiser, BlurKernel, "HFB", EvenFrame ? HFA : HFPrev);
        cmd.DispatchCompute(RecurrentDenoiser, BlurKernel, Mathf.CeilToInt(ScreenWidth / 16.0f), Mathf.CeilToInt(ScreenHeight / 16.0f), 1);

        cmd.SetComputeTextureParam(RecurrentDenoiser, SlowTemporalKernel, "Albedo", Albedo);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SlowTemporalKernel, "CurDepth", Depth);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SlowTemporalKernel, "Output", destination);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SlowTemporalKernel, "HFA", EvenFrame ? HFLAA : HFLAB);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SlowTemporalKernel, "HFPrev", EvenFrame ? HFLAB : HFLAA);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SlowTemporalKernel, "NormB", EvenFrame ? NormB : NormA);
        cmd.SetComputeTextureParam(RecurrentDenoiser, SlowTemporalKernel, "NormA", EvenFrame ? NormA : NormB);
        cmd.DispatchCompute(RecurrentDenoiser, SlowTemporalKernel, Mathf.CeilToInt(ScreenWidth / 16.0f), Mathf.CeilToInt(ScreenHeight / 16.0f), 1);

        cmd.Blit(Depth, PrevDepth);
    }
}
