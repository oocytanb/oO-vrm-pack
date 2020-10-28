/*
 * Copyright (c) 2019 oO (https://github.com/oocytanb)
 * MIT Licensed
 */

using System.IO;
using UnityEditor;
using UnityEngine;
using UniGLTF;
using VRM;

namespace cytanb
{
    public static class CytanbBindVrmComponentsMenu
    {
        const string ACTION_NAME = "Bind VRM Components";
        const string MENU_ITEM_KEY = "Cytanb/" + ACTION_NAME;

        [MenuItem(MENU_ITEM_KEY, true)]
        static bool ValidatBindVrmComponentsMenu()
        {
            var root = Selection.activeObject as GameObject;
            if (!root)
            {
                return false;
            }

            var animator = root.GetComponent<Animator>();
            if (!animator)
            {
                return false;
            }

            return true;
        }

        [MenuItem(MENU_ITEM_KEY, false, 1)]
        static void BindVrmComponentsMenu()
        {
            var longMsg = "";

            try
            {
                var groupId = Undo.GetCurrentGroup();

                var root = Selection.activeObject as GameObject;
                if (!root)
                {
                    EditorUtility.DisplayDialog("Error", "There is no selected object.", "OK");
                    return;
                }

                var prefab = ResolvePrefab(root.name);
                if (!prefab)
                {
                    var msg = "[Warning] " + root.name + ".prefab was not found.";
                    Debug.LogWarning(msg);
                    ShowNotificationMessage(msg);
                    return;
                }

                Undo.RecordObject(root, ACTION_NAME);

                // Meta
                {
                    var destComponent = root.GetComponent<VRMMeta>();
                    if (destComponent)
                    {
                        var msg = "[Skip] VRM Meta component already exists.";
                        longMsg += msg + "\n";
                        Debug.Log(msg);
                    }
                    else
                    {
                        var meta = CopyComponent<VRMMeta>(prefab, root);
                        if (meta)
                        {
                            var msg = "[OK] VRM Meta component was bound.";
                            longMsg += msg + "\n";
                            Debug.Log(msg);

                            // replace thumbnail
                            var targetThumbnail = ResolveThumbnail(meta.Meta.Thumbnail, prefab);
                            if (targetThumbnail != meta.Meta.Thumbnail)
                            {
                                var serMeta = new SerializedObject(meta.Meta);
                                var serThumbnail = serMeta.FindProperty("Thumbnail");
                                serThumbnail.objectReferenceValue = targetThumbnail;
                                serMeta.ApplyModifiedProperties();

                                var thumbMsg = "[OK] VRM Meta thumbnail was replaced.";
                                longMsg += thumbMsg + "\n";
                                Debug.Log(thumbMsg);
                            }
                        }
                    }
                }

                // Humanoid Description
                // if (root.GetComponent<VRMHumanoidDescription>())
                // {
                //     var msg = "[Skip] VRM Humanoid Description component already exists.";
                //     longMsg += msg + "\n";
                //     Debug.Log(msg);
                // }
                // else if (CopyComponent<VRMHumanoidDescription>(prefab, root))
                // {
                //     var msg = "[OK] VRM Humanoid Description component was bound.";
                //     longMsg += msg + "\n";
                //     Debug.Log(msg);
                // }

                // Blend Shape Proxy
                if (root.GetComponent<VRMBlendShapeProxy>())
                {
                    var msg = "[Skip] VRM Blend Shape Proxy component already exists.";
                    longMsg += msg + "\n";
                    Debug.Log(msg);
                }
                else if (CopyComponent<VRMBlendShapeProxy>(prefab, root))
                {
                    var msg = "[OK] VRM Blend Shape Proxy component was bound.";
                    longMsg += msg + "\n";
                    Debug.Log(msg);
                }

                // FirstPerson
                // if (root.GetComponent<VRMFirstPerson>())
                // {
                //     var msg = "[Skip] VRM First Person component already exists.";
                //     longMsg += msg + "\n";
                //     Debug.Log(msg);
                // }
                // else if (CopyComponent<VRMFirstPerson>(prefab, root))
                // {
                //     var msg = "[OK] VRM First Person component was bound.";
                //     longMsg += msg + "\n";
                //     Debug.Log(msg);
                // }

                // secondary
                if (root.transform.Find("secondary"))
                {
                    var msg = "[Skip] secondary object already exists.";
                    longMsg += msg + "\n";
                    Debug.Log(msg);
                }
                else
                {
                    var prefabSecondary = prefab.transform.Find("secondary").gameObject;
                    if (prefabSecondary)
                    {
                        GameObject secondary = GameObject.Instantiate(prefabSecondary);
                        secondary.transform.SetParent(root.transform, false);
                        secondary.transform.localPosition = prefabSecondary.transform.localPosition;
                        secondary.transform.localScale = prefabSecondary.transform.localScale;
                        secondary.name = "secondary";
                        Undo.RegisterCreatedObjectUndo(secondary, ACTION_NAME);

                        var msg = "[OK] secondary object was cloned.";
                        longMsg += msg + "\n";
                        Debug.Log(msg);
                    }
                }

                Undo.CollapseUndoOperations(groupId);
            }
            catch (System.Exception e)
            {
                longMsg += "Failed to bind components: Unsupported operation.";
                Debug.LogException(e);
            }

            if (!string.IsNullOrEmpty(longMsg))
            {
                ShowNotificationMessage(longMsg);
            }
        }

        private static void ShowNotificationMessage(string msg)
        {
            var assembly = typeof(UnityEditor.EditorWindow).Assembly;
            EditorWindow.GetWindow(assembly.GetType("UnityEditor.SceneView")).ShowNotification(new GUIContent(msg));
        }

        private static T CopyComponent<T>(GameObject src, GameObject dest) where T : Component
        {
            T srcComponent = src.GetComponent<T>();
            if (srcComponent == null)
            {
                return srcComponent;
            }

            T destComponent = dest.GetComponent<T>();
            if (destComponent == null)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(srcComponent);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(dest);
                return dest.GetComponent<T>();
            }
            else
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(srcComponent);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(destComponent);
                return destComponent;
            }
        }

        private static GameObject ResolvePrefab(string rootObjectName)
        {
            string prefabSearchName = rootObjectName + "-normalized";
            foreach (var guid in AssetDatabase.FindAssets("t:prefab " + prefabSearchName))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab && !string.IsNullOrEmpty(prefab.name) && prefabSearchName.ToLower().Equals(prefab.name.ToLower()))
                {
                    return prefab;
                }
            }
            return null;
        }

        private static Texture2D ResolveThumbnail(Texture2D thumbnail, GameObject prefab)
        {
            if (!thumbnail || string.IsNullOrEmpty(thumbnail.name))
            {
                return thumbnail;
            }

            if (!prefab)
            {
                return thumbnail;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return thumbnail;
            }

            string prefabTextureDir = Path.Combine(Path.GetDirectoryName(prefabPath), prefab.name + ".Textures");

            string thumbnailPath = AssetDatabase.GetAssetPath(thumbnail);
            if (string.IsNullOrEmpty(thumbnailPath))
            {
                return thumbnail;
            }

            string thumbnailDir = Path.GetDirectoryName(thumbnailPath);
            string thumbnailFileName = Path.GetFileName(thumbnailPath);
            if (thumbnailDir != prefabTextureDir)
            {
                return thumbnail;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D " + thumbnail.name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string dir = Path.GetDirectoryName(path);
                string fileName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(dir) || path == thumbnailPath || fileName != thumbnailFileName)
                {
                    continue;
                }

                var targetThumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (!targetThumbnail) {
                    continue;
                }
                
                if (targetThumbnail.imageContentsHash.Equals(thumbnail.imageContentsHash) || IsSameFacility(dir, thumbnailDir))
                {
                    // matched
                    return targetThumbnail;
                }
            }

            return thumbnail;
        }

        private static bool IsSameFacility(string dir1, string dir2)
        {
            if (dir1 == dir2)
            {
                return true;
            }

            if (string.IsNullOrEmpty(dir1) || string.IsNullOrEmpty(dir2))
            {
                return false;
            }

            string shortDir, longDir;
            if (dir1.Length < dir2.Length)
            {
                shortDir = dir1;
                longDir = dir2;
            }
            else
            {
                shortDir = dir2;
                longDir = dir1;
            }

            string shortParentDir = Path.GetDirectoryName(shortDir);
            if (string.IsNullOrEmpty(shortParentDir))
            {
                // need parent directory
                return false;
            }

            string longParentDir = Path.GetDirectoryName(longDir);
            if (string.IsNullOrEmpty(longParentDir))
            {
                // need parent directory
                return false;
            }

            return IsSameFacility(shortDir, longParentDir);
        }
    }
}
