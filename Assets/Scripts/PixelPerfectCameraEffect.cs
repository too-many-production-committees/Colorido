using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PixelPerfectCameraEffect : MonoBehaviour
{
    public bool enabledEffect = true;
    public int verticalResolution = 144;
    public FilterMode filterMode = FilterMode.Point;
    public Shader postShader;
    public float pixelJitter = 0.015f;
    public float colorSteps = 10f;
    public float noiseScale = 1f;
    public float noiseSpeed = 4f;

    private RenderTexture pixelTexture;
    private Material postMaterial;
    private int cachedWidth;
    private int cachedHeight;

    void OnDisable()
    {
        ReleaseRenderTexture();
        ReleaseMaterial();
    }

    void OnValidate()
    {
        verticalResolution = Mathf.Max(32, verticalResolution);
    }

    void Update()
    {
        if (pixelTexture != null)
            pixelTexture.filterMode = filterMode;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (!enabledEffect)
        {
            Graphics.Blit(source, destination);
            return;
        }

        UpdateRenderTexture(source.width, source.height);

        pixelTexture.filterMode = filterMode;
        Graphics.Blit(source, pixelTexture);

        Material material = GetPostMaterial();
        if (material != null)
        {
            material.SetFloat("_PixelJitter", pixelJitter);
            material.SetFloat("_ColorSteps", colorSteps);
            material.SetFloat("_NoiseScale", noiseScale);
            material.SetFloat("_NoiseSpeed", noiseSpeed);
            Graphics.Blit(pixelTexture, destination, material);
        }
        else
        {
            Graphics.Blit(pixelTexture, destination);
        }
    }

    void UpdateRenderTexture(int sourceWidth, int sourceHeight)
    {
        if (!enabledEffect)
            return;

        int height = Mathf.Max(32, verticalResolution);
        int width = Mathf.Max(32, Mathf.RoundToInt(height * (sourceWidth / (float)sourceHeight)));

        if (pixelTexture != null && cachedWidth == width && cachedHeight == height)
            return;

        ReleaseRenderTexture();

        cachedWidth = width;
        cachedHeight = height;
        pixelTexture = new RenderTexture(width, height, 0);
        pixelTexture.name = "PixelPerfectCameraTexture";
        pixelTexture.filterMode = filterMode;
        pixelTexture.wrapMode = TextureWrapMode.Clamp;
        pixelTexture.Create();
    }

    void ReleaseRenderTexture()
    {
        if (pixelTexture == null)
            return;

        if (Application.isPlaying)
            Destroy(pixelTexture);
        else
            DestroyImmediate(pixelTexture);

        pixelTexture = null;
    }

    Material GetPostMaterial()
    {
        if (postShader == null)
            postShader = Shader.Find("Custom/Pixel Perfect Post");

        if (postShader == null)
            return null;

        if (postMaterial == null || postMaterial.shader != postShader)
        {
            ReleaseMaterial();
            postMaterial = new Material(postShader);
            postMaterial.hideFlags = HideFlags.DontSave;
        }

        return postMaterial;
    }

    void ReleaseMaterial()
    {
        if (postMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(postMaterial);
        else
            DestroyImmediate(postMaterial);

        postMaterial = null;
    }
}
