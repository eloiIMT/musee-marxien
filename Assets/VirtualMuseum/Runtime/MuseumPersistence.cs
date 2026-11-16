using System;
using System.IO;
using UnityEngine;

namespace VirtualMuseum
{
    public static class MuseumPersistence
    {
        [Serializable]
        private sealed class SaveData
        {
            public int version;
            public Museum museum;
            public VisitorSession session;
        }
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "virtual-museum.json");
        public static string ToJson(MuseumApi api)
        {
            var errors = api.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            return JsonUtility.ToJson(new SaveData { version = 1, museum = api.Data, session = api.Session }, true);
        }
        public static bool TryLoadJson(MuseumApi api, string json, out string error)
        {
            SaveData save;
            try
            {
                if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("The save is empty.");
                save = JsonUtility.FromJson<SaveData>(json);
                if (save == null || save.version != 1 || save.museum == null || save.session == null)
                    throw new ArgumentException("Missing save data or unsupported version.");
                var errors = MuseumValidation.Validate(save.museum, save.session, api.Catalog);
                if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            { error = ex.Message; return false; }
            // Do not hide exceptions thrown by student event subscribers as file errors.
            api.Replace(save.museum, save.session);
            error = null; return true;
        }
        public static bool TrySave(MuseumApi api, out string error, string path = null)
        {
            try
            {
                path = Path.GetFullPath(path ?? DefaultPath);
                var json = ToJson(api);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                error = null; return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            { error = ex.Message; return false; }
        }
        public static bool TryLoad(MuseumApi api, out string error, string path = null)
        {
            string json;
            try { json = File.ReadAllText(path ?? DefaultPath); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            { error = ex.Message; return false; }
            return TryLoadJson(api, json, out error);
        }
    }
}
