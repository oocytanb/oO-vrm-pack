// SPDX-License-Identifier: MIT
// Copyright (c) 2021 oO (https://github.com/oocytanb)

using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRM;

namespace cytanb
{
    public static class CytanbMakeVrmPrefabMenu
    {
        readonly struct ActionResult
        {
            public readonly bool success;
            public readonly string value;

            public static readonly ActionResult Complete = new ActionResult(true, "[OK] Complete!");

            public static ActionResult Success(string value)
            {
                return new ActionResult(true, value);
            }

            public static ActionResult Fail(string message)
            {
                return new ActionResult(false, $@"[Fail] {message}");
            }

            public ActionResult(bool success, string value)
            {
                this.success = success;
                this.value = value;
            }

            public ActionResult AndThen(System.Func<string, ActionResult> f)
            {
                return success ? f(value) : this;
            }
        }

        class AssetFile
        {
            private static readonly bool HasAltDirectorySeparatorChar = Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar;

            private static readonly char[] DirectorySeparatorChars = HasAltDirectorySeparatorChar ?
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar } :
                new char[] { Path.DirectorySeparatorChar };

            private static readonly Regex ReplacePattern = new Regex(@"[/\\?|><:*""]", RegexOptions.Compiled);

            public static readonly System.Func<string, string> Replace = (str) => ReplacePattern.Replace(str, "_");

            public static readonly System.Func<string, string> Identity = (str) => str;

            public readonly Object asset;
            public readonly string path;
            public readonly string name;
            public readonly string dirName;
            public readonly string fileName;
            public readonly string extension;

            public AssetFile(Object asset, string path)
            {
                if (!asset || string.IsNullOrEmpty(path))
                {
                    throw new System.ArgumentException();
                }

                this.asset = asset;

                var np = NormalizeAssetPath(path, Replace);
                this.path = np;
                name = Path.GetFileNameWithoutExtension(np);
                dirName = NormalizeAssetPath(Path.GetDirectoryName(np));
                fileName = Path.GetFileName(np);
                extension = Path.GetExtension(np);
            }

            public static string NormalizeAssetPath(string path)
            {
                return NormalizeAssetPath(path, Identity);
            }

            /// <summary>
            /// 与えられたパス文字列を正規化します。
            /// カレントディレクトリ "." と親ディレクトリ ".." を解決します。
            /// パス区切り文字を "/" に置換します。
            /// 連続するパス区切り文字は 1 文字に連結します。
            /// 末尾のパス区切り文字は保持されます。
            /// null か空文字列が指定された場合は "." を返します。
            /// </summary>
            /// <param name="path">パス文字列</param>
            /// <param name="f">パスセグメントをマップする関数</param>
            /// <returns>正規化した文字列</returns>
            public static string NormalizeAssetPath(string path, System.Func<string, string> f)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return ".";
                }

                var hc = path[0];
                var isAbsolute = HasAltDirectorySeparatorChar ?
                    hc == Path.DirectorySeparatorChar || hc == Path.AltDirectorySeparatorChar :
                    hc == Path.DirectorySeparatorChar;
                if (isAbsolute && path.Length == 1)
                {
                    return "/";
                }

                var tc = path[path.Length - 1];
                var hasTrailingSeparator = HasAltDirectorySeparatorChar ?
                    tc == Path.DirectorySeparatorChar || tc == Path.AltDirectorySeparatorChar :
                    tc == Path.DirectorySeparatorChar;

                var entries = path.Split(DirectorySeparatorChars, System.StringSplitOptions.RemoveEmptyEntries);
                var stack = new Stack<string>(entries.Length);
                foreach (var entry in entries)
                {
                    var e = f(entry);
                    if (e == "..")
                    {
                        if (isAbsolute)
                        {
                            if (stack.Count >= 0)
                            {
                                stack.Pop();
                            }
                        }
                        else
                        {
                            if (stack.Count >= 1 && stack.Peek() != "..")
                            {
                                stack.Pop();
                            }
                            else
                            {
                                stack.Push(e);
                            }
                        }
                    }
                    else if (e != ".")
                    {
                        stack.Push(e);
                    }
                }

                if (stack.Count == 0)
                {
                    return isAbsolute ? "/" : hasTrailingSeparator ? "./" : ".";
                }

                var acc = stack.Pop() + (hasTrailingSeparator ? "/" : "");
                foreach (var entry in stack)
                {
                    acc = entry + "/" + acc;
                }

                if (isAbsolute)
                {
                    acc = "/" + acc;
                }

                return acc;
            }

            public GameObject AsGameObject()
            {
                return this.asset as GameObject;
            }

            public AssetFile Child(Object childAsset, string childPath)
            {
                return new AssetFile(childAsset, $@"{dirName}/{name}{childPath}");
            }

            public void Create()
            {
                if (!Directory.Exists(dirName))
                {
                    Directory.CreateDirectory(dirName);
                    AssetDatabase.Refresh();
                }
                AssetDatabase.CreateAsset(asset, path);
            }
        }

        const string ActionName = "Make VRM Prefab";
        const string MenuItemKey = "Cytanb/" + ActionName;
        const string AssetExtension = ".asset";
        const string PrefabExtension = ".prefab";
        const string MetaObjectDir = ".MetaObject";
        const string BlendShapeDir = ".BlendShapes";

        static void ShowNotificationMessage(string msg)
        {
            EditorWindow.GetWindow(typeof(SceneView)).ShowNotification(new GUIContent(msg));
        }

        static void ShowActionResult(ActionResult rslt)
        {
            var msg = rslt.value;
            if (!string.IsNullOrEmpty(msg))
            {
                ShowNotificationMessage(msg);
            }
        }

        static bool IsHumanoidObject(GameObject go_)
        {
            var anim_ = go_ ? go_.GetComponent<Animator>() : null;
            return anim_ && anim_.isHuman;
        }
        static bool IsPlainHumanoidObject(GameObject go_)
        {
            return go_ &&
                !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(go_)) &&
                IsHumanoidObject(go_) &&
                !go_.GetComponent<VRMMeta>();
        }

        static ActionResult MakeMeta(AssetFile af)
        {
            var root = af.AsGameObject();
            var meta = ScriptableObject.CreateInstance<VRMMetaObject>();
            meta.name = "Meta";

            var c = root.AddComponent<VRMMeta>();
            c.Meta = meta;

            af.Child(meta, $@"{MetaObjectDir}/{meta.name}{AssetExtension}").Create();

            return ActionResult.Complete;
        }
        static ActionResult MakeBlendShape(AssetFile af)
        {
            var root = af.AsGameObject();
            var blendShape = ScriptableObject.CreateInstance<BlendShapeAvatar>();
            blendShape.name = "BlendShape";
            blendShape.CreateDefaultPreset();
            foreach (var clip in blendShape.Clips)
            {
                af.Child(clip, $@"{BlendShapeDir}/{clip.name}{AssetExtension}").Create();
            }
            af.Child(blendShape, $@"{BlendShapeDir}/{blendShape.name}{AssetExtension}").Create();

            var proxy = root.AddComponent<VRMBlendShapeProxy>();
            proxy.BlendShapeAvatar = blendShape;

            return ActionResult.Complete;
        }

        static ActionResult MakeSecondary(AssetFile af)
        {
            var root = af.AsGameObject();

            var secondary = root.transform.Find("secondary");
            if (!secondary)
            {
                var go = new GameObject("secondary");
                secondary = go.transform;
                secondary.SetParent(root.transform, false);

                var springBone = go.AddComponent<VRMSpringBone>();
            }

            return ActionResult.Complete;
        }

        static ActionResult MakeFirstPerson(AssetFile af)
        {
            var root = af.AsGameObject();
            root.AddComponent<VRMFirstPerson>();

            var anim = root.GetComponent<Animator>();
            if (anim)
            {
                var leftEye = anim.GetBoneTransform(HumanBodyBones.LeftEye);
                var rightEye = anim.GetBoneTransform(HumanBodyBones.RightEye);
                if (leftEye && rightEye)
                {
                    root.AddComponent<VRMLookAtHead>();

                    var boneApplyer = root.AddComponent<VRMLookAtBoneApplyer>();
                    boneApplyer.LeftEye = OffsetOnTransform.Create(leftEye);
                    boneApplyer.RightEye = OffsetOnTransform.Create(rightEye);
                }
            }

            return ActionResult.Complete;
        }

        static ActionResult MakeAvatarComponents(AssetFile af)
        {
            return (IsHumanoidObject(af.AsGameObject()) ? ActionResult.Complete : ActionResult.Fail("Target is not Humanoid-model"))
                .AndThen(_ => MakeMeta(af))
                .AndThen(_ => MakeBlendShape(af))
                .AndThen(_ => MakeSecondary(af))
                .AndThen(_ => MakeFirstPerson(af));
        }

        static ActionResult MakePrefab(GameObject ho)
        {
            var path_ = AssetDatabase.GetAssetPath(ho);
            if (string.IsNullOrEmpty(path_))
            {
                return ActionResult.Fail("Invalid asset path");
            }

            var newPath = AssetDatabase.GenerateUniqueAssetPath(
                $@"{Path.GetDirectoryName(path_)}{Path.DirectorySeparatorChar}{Path.GetFileNameWithoutExtension(path_)}{PrefabExtension}"
            );

            var af = new AssetFile(PrefabUtility.InstantiatePrefab(ho), newPath);

            try
            {
                return MakeAvatarComponents(af)
                    .AndThen(_ =>
                    {
                        PrefabUtility.SaveAsPrefabAsset(af.AsGameObject(), af.path, out bool success);
                        return success ? ActionResult.Complete : ActionResult.Fail($@"Could not save Prefab: {af.path}");
                    });
            }
            finally
            {
                Object.DestroyImmediate(af.asset);
            }
        }

        [MenuItem(MenuItemKey, true)]
        static bool ValidateMenu()
        {
            return IsPlainHumanoidObject(Selection.activeObject as GameObject);
        }

        [MenuItem(MenuItemKey, false, 1)]
        static void MenuAction()
        {
            try
            {
                var ho_ = Selection.activeObject as GameObject;
                var rslt = (IsPlainHumanoidObject(ho_) ? ActionResult.Complete : ActionResult.Fail("There is no selected Humanoid-model"))
                    .AndThen(_ => MakePrefab(ho_));

                ShowActionResult(rslt);
            }
            catch (System.Exception ex)
            {
                ShowActionResult(ActionResult.Fail(ex.Message));
                Debug.LogException(ex);
            }
        }
    }
}
