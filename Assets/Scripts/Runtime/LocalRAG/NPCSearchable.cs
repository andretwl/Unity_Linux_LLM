using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
{
    #region Searchable (abstract base)

    [DefaultExecutionOrder(-2)]
    public abstract class NPCSearchable : MonoBehaviour
    {
        public abstract string Get(int key);
        public abstract Task<int> Add(string inputString, string group = "");
        public abstract int Remove(string inputString, string group = "");
        public abstract void Remove(int key);
        public abstract int Count();
        public abstract int Count(string group);
        public abstract void Clear();
        public abstract Task<int> IncrementalSearch(string queryString, string group = "");
        public abstract (string[], float[], bool) IncrementalFetch(int fetchKey, int k);
        public abstract (int[], float[], bool) IncrementalFetchKeys(int fetchKey, int k);
        public abstract void IncrementalSearchComplete(int fetchKey);

        public async Task<(string[], float[])> Search(string queryString, int k, string group = "")
        {
            int fetchKey = await IncrementalSearch(queryString, group);
            (string[] phrases, float[] distances, bool completed) = IncrementalFetch(fetchKey, k);
            if (!completed)
                IncrementalSearchComplete(fetchKey);
            return (phrases, distances);
        }

        public abstract void Save(ZipArchive archive);
        public abstract void Load(ZipArchive archive);

        public virtual string GetSavePath(string name) => Path.Combine(GetType().Name, name);

        public virtual void UpdateGameObjects() { }

        protected T ConstructComponent<T>(Type type, Action<T, T> copyAction = null)
            where T : Component
        {
            T Construct(Type t)
            {
                if (t == null)
                    return null;
                T newComponent = (T)gameObject.AddComponent(t);
                if (newComponent is NPCSearchable searchable)
                    searchable.UpdateGameObjects();
                return newComponent;
            }

            T component = (T)gameObject.GetComponent(typeof(T));
            T newComponent;
            if (component == null)
            {
                newComponent = Construct(type);
            }
            else
            {
                if (component.GetType() == type)
                {
                    newComponent = component;
                }
                else
                {
                    newComponent = Construct(type);
                    if (type != null)
                        copyAction?.Invoke(component, newComponent);
#if UNITY_EDITOR
                    DestroyImmediate(component);
#else
                    Destroy(component);
#endif
                }
            }
            return newComponent;
        }

        public virtual void Awake() => UpdateGameObjects();

#if UNITY_EDITOR
        public virtual void Reset()
        {
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update += UpdateGameObjects;
        }

        public virtual void OnDestroy()
        {
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update -= UpdateGameObjects;
        }
#endif

        public void SaveFile(string filePath)
        {
            try
            {
                string path = ResolveAssetPath(filePath);
                NPCArchiveSaver.Save(path, Save);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[NPC] File {filePath} could not be saved: {e.GetType()}: {e.Message}"
                );
            }
        }

        public Task<bool> LoadFile(string filePath)
        {
            try
            {
                string path = ResolveAssetPath(filePath);
                if (!File.Exists(path))
                    return Task.FromResult(false);
                NPCArchiveSaver.Load(path, Load);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[NPC] File {filePath} could not be loaded: {e.GetType()}: {e.Message}"
                );
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        public static string ResolveAssetPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;
            string normalized = relativePath.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
                return normalized;
            return Path.Combine(Application.streamingAssetsPath, normalized).Replace('\\', '/');
        }
    }

    #endregion

    #region NPCSearchMethod (abstract)

    public abstract class NPCSearchMethod : NPCSearchable
    {
        [FormerlySerializedAs("llmEmbedder")]
        [SerializeField]
        NPCLocalAIEmbedder _llmEmbedder;
        public NPCLocalAIEmbedder LlmEmbedder => _llmEmbedder;

        protected int nextKey = 0;
        protected int nextIncrementalSearchKey = 0;
        protected SortedDictionary<int, string> data = new SortedDictionary<int, string>();
        protected SortedDictionary<string, List<int>> dataSplits =
            new SortedDictionary<string, List<int>>();

        protected abstract void AddInternal(int key, float[] embedding);
        protected abstract void RemoveInternal(int key);
        protected abstract void ClearInternal();
        protected abstract void SaveInternal(ZipArchive archive);
        protected abstract void LoadInternal(ZipArchive archive);

        public async Task<(string[], float[])> SearchFromList(string query, string[] searchList)
        {
            float[] embedding = await Encode(query);
            float[][] embeddingsList = new float[searchList.Length][];
            for (int i = 0; i < searchList.Length; i++)
                embeddingsList[i] = await Encode(searchList[i]);

            float[] unsortedDistances = InverseDotProduct(embedding, embeddingsList);
            List<(string, float)> sortedLists = searchList
                .Zip(unsortedDistances, (first, second) => (first, second))
                .OrderBy(item => item.Item2)
                .ToList();

            string[] results = new string[sortedLists.Count];
            float[] distances = new float[sortedLists.Count];
            for (int i = 0; i < sortedLists.Count; i++)
            {
                results[i] = sortedLists[i].Item1;
                distances[i] = sortedLists[i].Item2;
            }
            return (results.ToArray(), distances.ToArray());
        }

        public static float DotProduct(float[] vector1, float[] vector2)
        {
            if (vector1 == null || vector2 == null)
            {
                Debug.LogError("Vectors cannot be null");
                return 0;
            }
            if (vector1.Length != vector2.Length)
            {
                Debug.LogError("Vector lengths must be equal");
                return 0;
            }
            float result = 0;
            for (int i = 0; i < vector1.Length; i++)
                result += vector1[i] * vector2[i];
            return result;
        }

        public static float InverseDotProduct(float[] vector1, float[] vector2) =>
            1 - DotProduct(vector1, vector2);

        public static float[] InverseDotProduct(float[] vector1, float[][] vector2)
        {
            float[] results = new float[vector2.Length];
            for (int i = 0; i < vector2.Length; i++)
                results[i] = InverseDotProduct(vector1, vector2[i]);
            return results;
        }

        public virtual async Task<float[]> Encode(string inputString)
        {
            if (_llmEmbedder == null)
            {
                Debug.LogError("[NPC] SearchMethod: _llmEmbedder is null, cannot encode.");
                return Array.Empty<float>();
            }
            return (await _llmEmbedder.Embeddings(inputString)).ToArray();
        }

        /// <summary>Simple tokenize: splits on whitespace/punctuation. For remote-only mode without LocalAI tokenize endpoint.</summary>
        public virtual async Task<List<int>> Tokenize(string query)
        {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(query))
                return new List<int>();
            // Simple character-level tokenization as fallback
            var tokens = new List<int>();
            for (int i = 0; i < query.Length; i++)
                tokens.Add((int)query[i]);
            return tokens;
        }

        /// <summary>Simple detokenize: reconstructs from char codes.</summary>
        public virtual async Task<string> Detokenize(List<int> tokens)
        {
            await Task.Yield();
            if (tokens == null || tokens.Count == 0)
                return string.Empty;
            char[] chars = new char[tokens.Count];
            for (int i = 0; i < tokens.Count; i++)
                chars[i] = (char)(tokens[i] & 0xFFFF);
            return new string(chars);
        }

        public override string Get(int key)
        {
            if (data.TryGetValue(key, out string result))
                return result;
            return null;
        }

        public override async Task<int> Add(string inputString, string group = "")
        {
            int key = nextKey++;
            AddInternal(key, await Encode(inputString));
            data[key] = inputString;
            if (!dataSplits.ContainsKey(group))
                dataSplits[group] = new List<int> { key };
            else
                dataSplits[group].Add(key);
            return key;
        }

        public override void Clear()
        {
            data.Clear();
            dataSplits.Clear();
            ClearInternal();
            nextKey = 0;
            nextIncrementalSearchKey = 0;
        }

        protected bool RemoveEntry(int key)
        {
            bool removed = data.Remove(key);
            if (removed)
                RemoveInternal(key);
            return removed;
        }

        public override void Remove(int key)
        {
            if (RemoveEntry(key))
            {
                foreach (var dataSplit in dataSplits.Values)
                    dataSplit.Remove(key);
            }
        }

        public override int Remove(string inputString, string group = "")
        {
            if (!dataSplits.TryGetValue(group, out List<int> dataSplit))
                return 0;
            List<int> removeIds = new List<int>();
            foreach (int key in dataSplit)
            {
                if (Get(key) == inputString)
                    removeIds.Add(key);
            }
            foreach (int key in removeIds)
            {
                if (RemoveEntry(key))
                    dataSplit.Remove(key);
            }
            return removeIds.Count;
        }

        public override int Count() => data.Count;

        public override int Count(string group)
        {
            if (!dataSplits.TryGetValue(group, out List<int> dataSplit))
                return 0;
            return dataSplit.Count;
        }

        public override async Task<int> IncrementalSearch(string queryString, string group = "")
        {
            return IncrementalSearch(await Encode(queryString), group);
        }

        public abstract int IncrementalSearch(float[] embedding, string group = "");

        public override (string[], float[], bool) IncrementalFetch(int fetchKey, int k)
        {
            (int[] keys, float[] distances, bool completed) = IncrementalFetchKeys(fetchKey, k);
            string[] results = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                results[i] = Get(keys[i]);
            return (results, distances, completed);
        }

        public override void Save(ZipArchive archive)
        {
            NPCArchiveSaver.Save(archive, data, GetSavePath("data"));
            NPCArchiveSaver.Save(archive, dataSplits, GetSavePath("dataSplits"));
            NPCArchiveSaver.Save(archive, nextKey, GetSavePath("nextKey"));
            NPCArchiveSaver.Save(
                archive,
                nextIncrementalSearchKey,
                GetSavePath("nextIncrementalSearchKey")
            );
            SaveInternal(archive);
        }

        public override void Load(ZipArchive archive)
        {
            data = NPCArchiveSaver.Load<SortedDictionary<int, string>>(
                archive,
                GetSavePath("data")
            );
            dataSplits = NPCArchiveSaver.Load<SortedDictionary<string, List<int>>>(
                archive,
                GetSavePath("dataSplits")
            );
            nextKey = NPCArchiveSaver.Load<int>(archive, GetSavePath("nextKey"));
            nextIncrementalSearchKey = NPCArchiveSaver.Load<int>(
                archive,
                GetSavePath("nextIncrementalSearchKey")
            );
            LoadInternal(archive);
        }

        public override void UpdateGameObjects()
        {
            if (this == null || _llmEmbedder != null)
                return;
            _llmEmbedder = ConstructComponent<NPCLocalAIEmbedder>(
                typeof(NPCLocalAIEmbedder),
                (
                    previous,
                    current
                ) => { /* no LLM ref to copy */
                }
            );
        }
    }

    #endregion

    #region NPCSearchPlugin (for chunking)

    public abstract class NPCSearchPlugin : NPCSearchable
    {
        protected NPCSearchMethod search;

        public void SetSearch(NPCSearchMethod search) => this.search = search;

        protected abstract void SaveInternal(ZipArchive archive);
        protected abstract void LoadInternal(ZipArchive archive);

        public override void Save(ZipArchive archive)
        {
            search.Save(archive);
            SaveInternal(archive);
        }

        public override void Load(ZipArchive archive)
        {
            search.Load(archive);
            LoadInternal(archive);
        }
    }

    #endregion

    #region NPCArchiveSaver

    public static class NPCArchiveSaver
    {
        public delegate void ArchiveSaverCallback(ZipArchive archive);

        public static void Save(string filePath, ArchiveSaverCallback callback)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                callback(archive);
            }
        }

        public static void Load(string filePath, ArchiveSaverCallback callback)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                callback(archive);
            }
        }

        public static void Save<T>(ZipArchive archive, T data, string path)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path);
            using (Stream stream = entry.Open())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, data);
            }
        }

        public static T Load<T>(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = archive.GetEntry(path);
            if (entry == null)
                return default;
            using (Stream stream = entry.Open())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                return (T)formatter.Deserialize(stream);
            }
        }
    }

    #endregion
}
