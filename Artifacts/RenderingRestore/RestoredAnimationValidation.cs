using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using CartoonProjection;
using UnityEngine.Rendering.Universal;

// Copy into Assets/Editor in an isolated project and invoke RestoredAnimationValidation.Run.
public static class RestoredAnimationValidation
{
    static readonly List<string> lines = new List<string>();
    const string Output = "Artifacts/RenderingRestore";
    static void Require(bool condition, string label) {
        if (!condition) throw new InvalidOperationException(label);
        lines.Add("PASS: " + label);
    }
    static void State(PlayerController player, string property, object value) {
        typeof(PlayerController).GetProperty(property).SetValue(player, value);
    }
    public static void Run() {
        Directory.CreateDirectory(Output);
        try {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            Require(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Main scene has no missing scripts");
            var visual = UnityEngine.Object.FindFirstObjectByType<PlayerBillboardVisual>();
            var player = visual.GetComponent<PlayerController>();
            var driver = visual.GetComponent<PlayerAnimationDriver>();
            Require(driver && visual.IsAnimatedModeActive, "Animation driver and controller are connected");
            visual.EnsureVisual(); visual.ApplySettings();
            var animator = visual.AnimatedAnimator;
            var sprite = visual.AnimatedSpriteRenderer;
            var controller = animator.runtimeAnimatorController as AnimatorController;
            var states = controller.layers[0].stateMachine.states.Select(s => s.state.name).OrderBy(s => s).ToArray();
            Require(string.Join(",",states) == "Idle,IdleVariant,Move", "Original Idle / IdleVariant / Move state machine preserved");
            Require(animator.parameters.Length == 7, "All 7 original Animator parameters preserved");
            player.enabled = false; driver.enabled = false;
            var update = typeof(PlayerAnimationDriver).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
            Action<float> tick = dt => { update.Invoke(driver,null); animator.Update(dt); };
            State(player,"IsGrounded",true); State(player,"IsInputPaused",false);
            animator.Rebind(); animator.Update(0);
            State(player,"HorizontalSpeed",player.moveSpeed);
            for(int i=0;i<5;i++) tick(1f/24);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Move"),"Grounded movement enters Move");
            animator.Play("Move",0,0f); animator.Update(0);
            var frames = new HashSet<Sprite>(); if(sprite.sprite) frames.Add(sprite.sprite);
            var moveClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Anime/Player/Clips/Move.anim");
            float sampleStep = 1f / (moveClip.frameRate * 4f);
            for(int i=0;i<Mathf.CeilToInt(moveClip.length * 1.2f / sampleStep);i++){ tick(sampleStep); if(sprite.sprite) frames.Add(sprite.sprite); }
            Require(frames.Count == 40, "Move plays all 40 sprite frames (observed " + frames.Count + ")");
            State(player,"IsRunning",true); tick(1f/24);
            Require(Mathf.Approximately(animator.GetFloat("MoveSpeed"),1.5f),"Run input increases animation playback speed");
            State(player,"FacingSign",-1f); tick(1f/24);
            Require(sprite.flipX==driver.artFacesRight,"Facing matches preserved artwork orientation");
            State(player,"IsInputPaused",true); tick(1f/24);
            Require(animator.speed==0f,"Camera input pause freezes animation");
            State(player,"IsInputPaused",false); tick(1f/24);
            Require(animator.speed==1f,"Animation resumes after pause");
            State(player,"HorizontalSpeed",0f); State(player,"IsRunning",false);
            driver.idleStartDelay=4f;
            for(int i=0;i<6;i++) tick(1f/24);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Stopping enters default Idle before random selection");
            driver.idleStartDelay=0f; driver.idleVariantChance=1f;
            animator.Play("Move",0,0f); animator.Update(0);
            for(int i=0;i<6;i++) tick(1f/24);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("IdleVariant"),"Idle driver selects IdleVariant");
            int jumpCount=player.JumpCount;
            typeof(PlayerController).GetMethod("ReportJump",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,null);
            tick(1f/24);
            Require(player.JumpCount==jumpCount+1 && !animator.GetBool("Grounded"),"Jump telemetry reaches animation driver");
            lines.Add("INFO: Backup has no Jump or Fall clips/states; placeholders remain unchanged.");
            var camera = Camera.main;
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset");
            var feature = data.rendererFeatures.OfType<CartoonRendererFeature>().Single();
            var target = new RenderTexture(800,600,24); target.Create(); camera.targetTexture=target;
            ShaderUtil.allowAsyncCompilation=false;
            ProjectedShapePass.UseSynchronousReadback=true;
            feature.settings.enabled=true; feature.settings.sceneChannelEnabled=true;
            for(int i=0;i<8;i++){camera.Render();ProjectedShapePass.ResolveSynchronousCapture();}
            var previous=RenderTexture.active; RenderTexture.active=target;
            var texture = new Texture2D(800,600,TextureFormat.RGBA32,false);
            texture.ReadPixels(new Rect(0,0,800,600),0,0); texture.Apply();
            File.WriteAllBytes(Output+"/SampleScene.png",texture.EncodeToPNG());
            RenderTexture.active=previous; camera.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target);
            Require(ProjectedShapePass.Regions>0,"Restored main scene produces projected scene geometry");
            lines.Add("Unity version: "+Application.unityVersion);
            lines.Add("RESULT: PASS");
            File.WriteAllLines(Output+"/AnimationReport.txt",lines); EditorApplication.Exit(0);
        } catch(Exception e){ lines.Add("RESULT: FAIL "+e); File.WriteAllLines(Output+"/AnimationReport.txt",lines); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
