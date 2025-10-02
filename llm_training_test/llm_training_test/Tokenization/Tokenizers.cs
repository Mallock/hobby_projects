using System;
using System.Collections.Generic;

namespace TinyGptDemo.Tokenization
{
    public enum TokenizationMode
    {
        Word,
        Sentence
    }

    public static class TokenizationModeExtensions
    {
        public static TokenizationMode ParseOrDefault(string? value, TokenizationMode defaultMode)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultMode;

            value = value.Trim().ToLowerInvariant();
            return value switch
            {
                "sentence" => TokenizationMode.Sentence,
                "s" => TokenizationMode.Sentence,
                "word" => TokenizationMode.Word,
                "w" => TokenizationMode.Word,
                _ => defaultMode
            };
        }

        public static string ToDescription(this TokenizationMode mode) =>
            mode == TokenizationMode.Sentence ? "sentence" : "word";
    }

    public interface ITokenizer
    {
        List<string> Tokenize(string text);
    }

    public static class TokenizerFactory
    {
        public static ITokenizer Create(TokenizationMode mode) =>
            mode == TokenizationMode.Sentence ? new SentenceTokenizer() : new WordTokenizer();
    }

    public static class SpecialTokens
    {
        public const string WordEos = "<EOS_W>";
        public const string SentenceEos = "<EOS_S>";
    }

    internal sealed class WordTokenizer : ITokenizer
    {
        public List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            int i = 0;

            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }

                if (char.IsPunctuation(text[i]) || char.IsSymbol(text[i]))
                {
                    tokens.Add(text[i].ToString());
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length &&
                       !char.IsWhiteSpace(text[i]) &&
                       !char.IsPunctuation(text[i]) &&
                       !char.IsSymbol(text[i]))
                {
                    i++;
                }

                tokens.Add(text.Substring(start, i - start));
            }

            return tokens;
        }
    }

    internal sealed class SentenceTokenizer : ITokenizer
    {
        private static readonly char[] SentenceTerminators = { '.', '!', '?', '…' };

        public List<string> Tokenize(string text)
        {
            var sentences = new List<string>();
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (Array.IndexOf(SentenceTerminators, text[i]) >= 0)
                {
                    int end = i + 1;
                    string candidate = text.Substring(start, end - start).Trim();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        sentences.Add(candidate);
                    }

                    start = end;
                }
            }

            if (start < text.Length)
            {
                string tail = text.Substring(start).Trim();
                if (!string.IsNullOrWhiteSpace(tail))
                {
                    sentences.Add(tail);
                }
            }

            return sentences;
        }
    }
}