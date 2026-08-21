using System;
using System.Collections.Generic;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>Intermediate representation after normalize (sus-design/v1 / DTCG / Tokens Studio).</summary>
    public sealed class DesignDocument
    {
        public string Schema { get; set; } = "sus-design/v1";
        public DesignSource Source { get; set; } = new DesignSource();
        public List<DesignToken> Tokens { get; set; } = new List<DesignToken>();
        public List<DesignMode> Modes { get; set; } = new List<DesignMode>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class DesignSource
    {
        public string Tool { get; set; } = "unknown";
        public string File { get; set; } = "";
    }

    public sealed class DesignToken
    {
        /// <summary>Canonical path like color.primary or dimension.space.16.</summary>
        public string Path { get; set; } = "";
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class DesignMode
    {
        public string Name { get; set; } = "";
        public string AppliesTo { get; set; } = "breakpoint-sm";
        public List<DesignToken> Tokens { get; set; } = new List<DesignToken>();
    }

    public sealed class MappedToken
    {
        public string AliasPath { get; set; } = "";
        public string CssVar { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsDownstream { get; set; }
    }

    public sealed class ImportResult
    {
        public bool Ok { get; set; }
        public string Uss { get; set; } = "";
        public string MetaJson { get; set; } = "";
        public List<MappedToken> Mapped { get; set; } = new List<MappedToken>();
        public List<string> Skipped { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public string InputSha256 { get; set; } = "";
    }

    public sealed class ValidateResult
    {
        public bool Ok { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> UnknownAliases { get; set; } = new List<string>();
        public List<string> GhostCssVars { get; set; } = new List<string>();
    }

    public sealed class ImportOptions
    {
        public string OutDir { get; set; } = "";
        public string UssFileName { get; set; } = "imported-tokens.uss";
        public string MetaFileName { get; set; } = ".sus-design-meta.json";
        public bool DryRun { get; set; }
        public bool EmitUnknown { get; set; }
        /// <summary>When true, alias rows with cssPrefix downstream (--sk-*) are allowed.</summary>
        public bool Downstream { get; set; }
        public string AliasMapPath { get; set; } = "";
        public DateTime? TimestampUtc { get; set; }
    }
}
