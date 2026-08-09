using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;

namespace SoflanSupport
{
    public struct SoflanCompositionLoadResult
    {
        public int ParsedCount;
        public string FailedLine;

        public bool Success => FailedLine == null;
    }

    /// <summary>
    /// 游戏运行时与集成测试共享的 MA2 SFL 行装载器。
    /// 原版 MA2RecordID 不认识 SFL，因此不能从 MA2RecordList 恢复这些行；
    /// 文件 IO 由调用方负责，本类型只负责逐行解析和写入 SoflanListMap。
    /// </summary>
    public static class SoflanCompositionParser
    {
        public static SoflanCompositionLoadResult Load(
            TextReader reader,
            SoflanListMap target,
            Action<string, ISoflan> onParsed = null)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            return LoadLines(ReadLines(reader), target, onParsed);
        }

        public static SoflanCompositionLoadResult LoadLines(
            IEnumerable<string> lines,
            SoflanListMap target,
            Action<string, ISoflan> onParsed = null)
        {
            if (lines == null)
                throw new ArgumentNullException(nameof(lines));
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            var result = new SoflanCompositionLoadResult();
            foreach (var line in lines)
            {
                if (line == null
                    || !line.StartsWith("SFL", StringComparison.InvariantCultureIgnoreCase))
                    continue;

                if (!TryParseSoflanLine(line, out var soflan))
                {
                    result.FailedLine = line;
                    return result;
                }

                target.Add(soflan);
                result.ParsedCount++;
                onParsed?.Invoke(line, soflan);
            }

            return result;
        }

        public static bool TryParseSoflanLine(string line, out ISoflan soflan)
        {
            try
            {
                soflan = new Soflan
                {
                    TGrid = new TGrid(
                        ParseIntField(line, 1),
                        ParseIntField(line, 2)),
                    Speed = ParseFloatField(line, 4),
                    SoflanGroup = 0
                };
                soflan.EndTGrid = soflan.TGrid + new GridOffset(0, ParseIntField(line, 3));

                var group = GetTabField(line, 5);
                if (!string.IsNullOrWhiteSpace(group))
                {
                    soflan.SoflanGroup = int.Parse(
                        group,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture);
                }
                return true;
            }
            catch
            {
                soflan = default;
                return false;
            }
        }

        private static IEnumerable<string> ReadLines(TextReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
                yield return line;
        }

        private static int ParseIntField(string line, int fieldIndex)
        {
            return int.Parse(
                GetTabField(line, fieldIndex),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        private static float ParseFloatField(string line, int fieldIndex)
        {
            return float.Parse(
                GetTabField(line, fieldIndex),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private static string GetTabField(string line, int fieldIndex)
        {
            var start = 0;
            var currentIndex = 0;
            for (var i = 0; i <= line.Length; i++)
            {
                if (i < line.Length && line[i] != '\t')
                    continue;

                if (currentIndex == fieldIndex)
                    return line.Substring(start, i - start).Trim();

                start = i + 1;
                currentIndex++;
            }

            return null;
        }
    }
}
