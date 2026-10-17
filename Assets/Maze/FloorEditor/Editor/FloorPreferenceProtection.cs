#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace Maze.FloorEditing.Editor
{
    // Complete native enumeration, rather than a hard-coded list of known game keys.
    // Private snapshots contain the user's save: keep them under Library, never upload them.
    [Serializable] public sealed class FloorPreferenceValue
    {
        public string key, type, text;
        public int integer;
        public float number;
    }

    [Serializable] public sealed class FloorPreferenceSnapshot
    {
        public string domain;
        public List<FloorPreferenceValue> entries = new();
    }

    public static class FloorPreferenceProtection
    {
        public static string Domain => "unity." + Application.companyName + "." + Application.productName;
        public static string NativePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Preferences", Domain + ".plist");

        public static FloorPreferenceSnapshot Capture()
        {
            RequireMac();
            PlayerPrefs.Save();
            var result = new FloorPreferenceSnapshot { domain = Domain };
            foreach (var pair in NativeValues().OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var value = new FloorPreferenceValue { key = pair.Key, type = pair.Value };
                if (value.type == "string") value.text = PlayerPrefs.GetString(value.key);
                else if (value.type == "integer") value.integer = PlayerPrefs.GetInt(value.key);
                else if (value.type == "real") value.number = PlayerPrefs.GetFloat(value.key);
                result.entries.Add(value);
            }
            return result;
        }

        public static string Digest(FloorPreferenceSnapshot value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(value)))).Replace("-", "").ToLowerInvariant();
        }

        public static void Restore(FloorPreferenceSnapshot snapshot)
        {
            RequireMac();
            if (snapshot == null || snapshot.domain != Domain) throw new InvalidOperationException("The playtest preference backup belongs to a different application.");
            PlayerPrefs.Save();
            var original = new HashSet<string>(snapshot.entries.Select(x => x.key), StringComparer.Ordinal);
            // Delete only keys created during this protected transaction; preserve unknown original keys too.
            foreach (string key in NativeValues().Keys) if (!original.Contains(key)) PlayerPrefs.DeleteKey(key);
            foreach (var value in snapshot.entries)
            {
                if (value.type == "string") PlayerPrefs.SetString(value.key, value.text ?? "");
                else if (value.type == "integer") PlayerPrefs.SetInt(value.key, value.integer);
                else if (value.type == "real") PlayerPrefs.SetFloat(value.key, value.number);
                else throw new InvalidOperationException("Unsupported preference type in recovery backup: " + value.type);
            }
            PlayerPrefs.Save();
            var current = Capture();
            if (Digest(current) != Digest(snapshot))
                throw new InvalidOperationException("Playtest preference recovery did not verify (" + Differences(snapshot, current) + "). The private backup has been retained.");
        }

        static void RequireMac()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
                throw new NotSupportedException("Protected playtest currently requires the macOS Editor so every local game preference can be captured and restored.");
        }

        static Dictionary<string, string> NativeValues()
        {
            // PlayerPrefs.Save can return before cfprefsd updates the on-disk plist. Enumerate
            // the live domain in THIS Unity process; a separate plutil process alone is stale.
            var values = CorePreferences.Values(Domain);
            foreach (var pair in DiskValues()) values.TryAdd(pair.Key, pair.Value);
            // The native file can also lag a deletion. The in-process PlayerPrefs cache decides
            // presence; stale file-only entries must not resurrect a key during verification.
            foreach (string key in values.Keys.ToArray()) if (!PlayerPrefs.HasKey(key)) values.Remove(key);
            return values;
        }

        public static string Differences(FloorPreferenceSnapshot expected, FloorPreferenceSnapshot actual)
        {
            var a = expected.entries.ToDictionary(x => x.key, StringComparer.Ordinal);
            var b = actual.entries.ToDictionary(x => x.key, StringComparer.Ordinal);
            var changes = new List<string>();
            foreach (string key in a.Keys.Except(b.Keys)) changes.Add("missing key " + key);
            foreach (string key in b.Keys.Except(a.Keys)) changes.Add("new key " + key);
            foreach (string key in a.Keys.Intersect(b.Keys))
                if (JsonUtility.ToJson(a[key]) != JsonUtility.ToJson(b[key])) changes.Add("value/type changed for " + key);
            return string.Join("; ", changes.Take(12));
        }

        static Dictionary<string, string> DiskValues()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(NativePath)) return values;
            // plutil is read-only here. Arguments are passed directly; no shell or interpolation runs.
            var start = new ProcessStartInfo("/usr/bin/plutil")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
                Arguments = "-convert xml1 -o - " + Quote(NativePath),
            };
            using var process = Process.Start(start);
            if (process == null) throw new IOException("Could not read the native game preference domain.");
            string xml = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000)) { process.Kill(); throw new IOException("Timed out reading native game preferences."); }
            if (process.ExitCode != 0) throw new IOException("Could not enumerate native game preferences: " + error.Trim());
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var document = XDocument.Load(reader);
            var dict = document.Root?.Element("dict") ?? throw new IOException("Native game preferences are not a dictionary.");
            var elements = dict.Elements().ToArray();
            if (elements.Length % 2 != 0) throw new IOException("Native game preferences contain an incomplete entry.");
            for (int i = 0; i < elements.Length; i += 2)
            {
                if (elements[i].Name.LocalName != "key") throw new IOException("Native game preference key is malformed.");
                string type = elements[i + 1].Name.LocalName;
                if (type != "integer" && type != "real" && type != "string")
                    throw new IOException("Cannot protect unsupported native preference type '" + type + "'. No playtest has started.");
                if (!values.TryAdd(elements[i].Value, type)) throw new IOException("Native preferences contain duplicate keys.");
            }
            return values;
        }

        static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static class CorePreferences
        {
            const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
            const uint Utf8 = 0x08000100;
            static readonly IntPtr Handle = dlopen(CF, 1);
            static readonly IntPtr CurrentUser = Symbol("kCFPreferencesCurrentUser");
            static readonly IntPtr AnyHost = Symbol("kCFPreferencesAnyHost");

            static IntPtr Symbol(string name)
            {
                if (Handle == IntPtr.Zero) throw new IOException("CoreFoundation could not be loaded for complete preference protection.");
                var address = dlsym(Handle, name);
                if (address == IntPtr.Zero) throw new IOException("The native preference constant " + name + " is unavailable.");
                return Marshal.ReadIntPtr(address);
            }

            public static Dictionary<string, string> Values(string domain)
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                var application = CFStringCreateWithCString(IntPtr.Zero, domain, Utf8);
                IntPtr keys = IntPtr.Zero;
                if (application == IntPtr.Zero) throw new IOException("Could not create the native game preference domain.");
                try
                {
                    if (!CFPreferencesAppSynchronize(application) || !CFPreferencesSynchronize(application, CurrentUser, AnyHost))
                        throw new IOException("The native game preference domain could not be synchronized.");
                    keys = CFPreferencesCopyKeyList(application, CurrentUser, AnyHost);
                    if (keys == IntPtr.Zero) return result;
                    long count = CFArrayGetCount(keys);
                    for (long i = 0; i < count; i++)
                    {
                        var key = CFArrayGetValueAtIndex(keys, i);
                        var value = CFPreferencesCopyValue(key, application, CurrentUser, AnyHost);
                        if (value == IntPtr.Zero) continue;
                        try
                        {
                            ulong kind = CFGetTypeID(value);
                            string type;
                            if (kind == CFStringGetTypeID()) type = "string";
                            else if (kind == CFNumberGetTypeID()) type = CFNumberIsFloatType(value) ? "real" : "integer";
                            else throw new IOException("Unsupported native preference type; no playtest has started.");
                            result.Add(String(key), type);
                        }
                        finally { CFRelease(value); }
                    }
                    return result;
                }
                finally { if (keys != IntPtr.Zero) CFRelease(keys); CFRelease(application); }
            }

            static string String(IntPtr value)
            {
                long capacity = CFStringGetMaximumSizeForEncoding(CFStringGetLength(value), Utf8) + 1;
                if (capacity < 1 || capacity > 1048576) throw new IOException("Native preference key is too large.");
                var bytes = new byte[(int)capacity];
                if (!CFStringGetCString(value, bytes, capacity, Utf8)) throw new IOException("Could not decode a native preference key.");
                int length = Array.IndexOf(bytes, (byte)0);
                return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
            }

            [DllImport(SystemLibrary)] static extern IntPtr dlopen(string path, int flags);
            [DllImport(SystemLibrary)] static extern IntPtr dlsym(IntPtr handle, string name);
            [DllImport(CF)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
            [DllImport(CF)] static extern long CFStringGetLength(IntPtr value);
            [DllImport(CF)] static extern long CFStringGetMaximumSizeForEncoding(long length, uint encoding);
            [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFStringGetCString(IntPtr value, [Out] byte[] bytes, long capacity, uint encoding);
            [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFPreferencesAppSynchronize(IntPtr application);
            [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFPreferencesSynchronize(IntPtr application, IntPtr user, IntPtr host);
            [DllImport(CF)] static extern IntPtr CFPreferencesCopyKeyList(IntPtr application, IntPtr user, IntPtr host);
            [DllImport(CF)] static extern IntPtr CFPreferencesCopyValue(IntPtr key, IntPtr application, IntPtr user, IntPtr host);
            [DllImport(CF)] static extern long CFArrayGetCount(IntPtr array);
            [DllImport(CF)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
            [DllImport(CF)] static extern ulong CFGetTypeID(IntPtr value);
            [DllImport(CF)] static extern ulong CFStringGetTypeID();
            [DllImport(CF)] static extern ulong CFNumberGetTypeID();
            [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFNumberIsFloatType(IntPtr value);
            [DllImport(CF)] static extern void CFRelease(IntPtr value);
        }
    }
}
#endif
