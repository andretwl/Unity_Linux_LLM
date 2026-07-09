using System;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPCSystem.Tests
{
    internal static class NPCTestHelpers
    {
        public const string MainScenePath = "Assets/Scenes/NPCDialoguePrototype1.unity";

        public static T CreateComponent<T>(string name = null) where T : Component
        {
            var gameObject = new GameObject(name ?? typeof(T).Name);
            return gameObject.AddComponent<T>();
        }

        public static void Destroy(params UnityEngine.Object[] objects)
        {
            foreach (UnityEngine.Object obj in objects)
            {
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
        }

        public static Scene OpenMainScene()
        {
            return EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        public static GameObject RequireGameObject(string path)
        {
            GameObject gameObject = GameObject.Find(path);
            if (gameObject == null)
            {
                throw new AssertionException($"Required GameObject not found in scene: {path}");
            }

            return gameObject;
        }

        public static T RequireComponent<T>(string gameObjectPath) where T : Component
        {
            GameObject gameObject = RequireGameObject(gameObjectPath);
            T component = gameObject.GetComponent<T>();
            if (component == null)
            {
                throw new AssertionException($"Required component {typeof(T).Name} not found on {gameObjectPath}.");
            }

            return component;
        }

        /// <summary>
        /// Create a unique temporary directory and return its full path.
        /// Caller is responsible for cleanup with Directory.Delete(dir, recursive: true).
        /// </summary>
        public static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "NPCTest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Create a minimal NPCProfile for testing without loading from asset.
        /// </summary>
        public static NPCProfile CreateMinimalProfile(string slug, string displayName)
        {
            var profile = ScriptableObject.CreateInstance<NPCProfile>();
            profile.NpcSlug = slug;
            profile.DisplayName = displayName;
            profile.systemPrompt = "You are a helpful NPC.";
            profile.maxTokens = 64;
            profile.ragResults = 1;
            profile.historySaveFile = $"NPCDialogue/{slug}.json";
            return profile;
        }
    }
}
