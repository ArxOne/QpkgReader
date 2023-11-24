namespace ArxOne.Qnap;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Formats.Tar;
using System.IO.Compression;

public class QpkgReader
{
    private readonly Stream _stream;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="QpkgReader"/> class.
    /// </summary>
    /// <param name="inputStream">The input stream of qpkg package</param>
    public QpkgReader(Stream inputStream)
    {
        _stream = inputStream;
    }


    /// <summary>
    /// Reads the configuration.
    /// </summary>
    /// <returns>Qpkg configuration</returns>
    /// <exception cref="System.FormatException">No script_len value found</exception>
    public IDictionary<string, string> ReadConfig()
    {
        var scriptLen = 0;
        using var reader = new StreamReader(_stream);
        while (!reader.EndOfStream)
        {
            var entryArray = reader.ReadLine()?.Split('=');
            if (entryArray is not { Length: 2 } || entryArray[0].Trim() != "script_len")
                continue;
            scriptLen = int.Parse(entryArray[1].Trim());
            break;
        }

        if (scriptLen == 0)
            throw new FormatException("No script_len value found");

        return GetConfiguration(scriptLen);
    }

    public static IDictionary<string, string> ReadPackageInfo(Stream inputStream)
    {
        var package = new QpkgReader(inputStream);
        return package.ReadConfig();
    }

    /// <summary>
    /// Gets the configuration.
    /// </summary>
    /// <param name="scriptLen">Length of the script.</param>
    /// <returns></returns>
    /// <exception cref="System.FormatException">No control found</exception>
    private IDictionary<string, string> GetConfiguration(int scriptLen)
    {
        _stream.Seek(scriptLen, SeekOrigin.Begin);
        using var tarReader = new TarReader(_stream);
        var entry = tarReader.GetNextEntry(true);

        var entryDataStream = entry?.DataStream ?? throw new FormatException("No control found");
        if (entry!.Name.EndsWith(".gz"))
            entryDataStream = new GZipStream(entryDataStream, CompressionMode.Decompress);
        using var subTarReader = new TarReader(entryDataStream);
        var rawConfiguration = GetRawConfiguration(subTarReader);
        return ParseConfiguration(rawConfiguration);
    }

    /// <summary>
    /// Gets the raw configuration.
    /// </summary>
    /// <param name="subTarReader">The sub tar reader.</param>
    /// <returns></returns>
    /// <exception cref="System.FormatException">No qpkg.cfg found</exception>
    private static string GetRawConfiguration(TarReader subTarReader)
    {
        while (subTarReader.GetNextEntry(true) is { } subEntry)
        {
            var subEntryName = subEntry.Name;
            if (subEntryName.StartsWith("./"))
                subEntryName = subEntryName[2..];
            if (subEntryName != "qpkg.cfg")
                continue;
            if (subEntry.DataStream == null)
                continue;
            using var configReader = new StreamReader(subEntry.DataStream);
            return configReader.ReadToEnd();
        }
        throw new FormatException("No qpkg.cfg found");
    }


    /// <summary>
    /// Parses the configuration.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="separator">The separator.</param>
    /// <returns></returns>
    private static IDictionary<string, string> ParseConfiguration(string config, char separator = '=')
    {
        var configuration = new Dictionary<string, string> { [""] = config };
        foreach (var line in config.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(v => !v.StartsWith('#')))
        {
            var elements = line.Split(separator);
            if (elements.Length != 2)
                continue;
            configuration[elements[0]] = elements[1].Trim(" \t\n\r\0\x0B\"".ToCharArray());
        }
        return configuration;
    }
}