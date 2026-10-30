using System;
using System.Collections.Generic;
using UnityEngine;

namespace Olympus.Game.Territory
{
    /// <summary>파일 한 줄. JsonUtility가 읽는 형태라 필드가 평평하고 public이어야 한다.</summary>
    [Serializable]
    public sealed class LocalizedStringRecord
    {
        public string key;
        public string text;
    }

    [Serializable]
    public sealed class LocalizedStringFile
    {
        public List<LocalizedStringRecord> strings = new List<LocalizedStringRecord>();
    }

    /// <summary>
    /// 화면 문구 키→텍스트 표. <see cref="Olympus.Core.Territory.BuildingDef.DisplayNameKey"/>가
    /// 문구 자체가 아니라 키인 이유가 이거다 — 문구를 코드에 박지 않고 데이터 파일
    /// (Assets/Data/strings-ko.json)에서 읽는다.
    ///
    /// 키가 없으면 키 자체를 돌려준다. 화면이 비어 있는 것보다 "번역 빠짐"이 눈에 띄는 게 낫다.
    /// </summary>
    public sealed class LocalizedStrings
    {
        private readonly Dictionary<string, string> _byKey = new Dictionary<string, string>();

        public static LocalizedStrings Load(TextAsset json)
        {
            var table = new LocalizedStrings();

            if (json == null)
                return table;

            LocalizedStringFile file = JsonUtility.FromJson<LocalizedStringFile>(json.text);

            if (file == null || file.strings == null)
                return table;

            for (int i = 0; i < file.strings.Count; i++)
            {
                LocalizedStringRecord r = file.strings[i];

                if (!string.IsNullOrEmpty(r.key))
                    table._byKey[r.key] = r.text;
            }

            return table;
        }

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "";

            string text;
            return _byKey.TryGetValue(key, out text) ? text : key;
        }
    }
}
