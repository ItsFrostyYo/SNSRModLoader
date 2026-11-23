using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SNSRModLoader.Updater
{
    internal sealed class FileTransaction
    {
        private readonly Dictionary<string, Action> writes = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        internal void Copy(string source, string target)
        { Add(target, delegate { Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target, true); }); }
        internal void Delete(string target)
        { Add(target, delegate { if (File.Exists(target)) File.Delete(target); }); }
        internal void Bytes(string target, byte[] value)
        { Add(target, delegate { Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllBytes(target, value); }); }
        private void Add(string target, Action write)
        {
            target = Path.GetFullPath(target);
            if (writes.ContainsKey(target)) throw new IOException("Two updates target the same file: " + target);
            writes.Add(target, write);
        }
        internal void Apply(Action<int> afterWrite = null)
        {
            var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long size = 0;
            foreach (var target in writes.Keys)
            {
                if (File.Exists(target)) {
                    size += new FileInfo(target).Length;
                    if (size > 256L * 1024 * 1024) throw new IOException("Update rollback data exceeds 256 MiB; no files were changed.");
                }
                originals.Add(target, File.Exists(target) ? File.ReadAllBytes(target) : null);
            }
            var attempted = new List<string>();
            try
            {
                int count = 0;
                foreach (var write in writes) { attempted.Add(write.Key); write.Value(); if (afterWrite != null) afterWrite(++count); }
            }
            catch (Exception original)
            {
                var errors = new List<Exception> { original };
                attempted.Reverse();
                foreach (var target in attempted)
                    try {
                        var old = originals[target];
                        if (old == null) { if (File.Exists(target)) File.Delete(target); }
                        else if (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(old)) File.WriteAllBytes(target, old);
                    }
                    catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) throw new AggregateException("Update failed and rollback needs manual recovery. Staged files were kept.", errors);
                throw new IOException("Update failed; previous loader and mod files were restored.", original);
            }
        }
    }
}
