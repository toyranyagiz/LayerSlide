using System;
using System.Collections.Generic;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>Baked meshes keyed the same way <see cref="MeshLibrary"/> names them.</summary>
    public sealed class MeshBank : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public Mesh mesh;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public void SetEntries(List<Entry> values) => entries = values;
    }
}
