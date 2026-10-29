using System;
using System.Collections.Generic;

namespace InputPolish
{
    /// <summary>一种网页总结方案：id 稳定（扩展、IPC、托盘都用它），label 给用户看。</summary>
    public class PageMode
    {
        public string Id;
        public string Label;
        public string Hint;
        public string Prompt;
        public int MaxTokens;

        public PageMode(string id, string label, string hint, int maxTokens, string prompt)
        {
            Id = id;
            Label = label;
            Hint = hint;
            MaxTokens = maxTokens;
            Prompt = prompt;
        }
    }

    /// <summary>
    /// 网页总结方案表。每个方案就是一段专门的提示词 —— 同一个页面，换个方案就换个视角。
    /// 顺序即 UI 里 chip 的顺序，第一个是默认方案。
    /// </summary>
    public static class PageModes
    {
        public const string Custom = "custom";

        private static readonly PageMode[] Table = new PageMode[]
        {
            new PageMode("brief", "简明总结", "一句话主旨 + 3~5 段", 2600,
                "请对下面这篇网页写一份简明总结：先用一句话点出全文主旨，再用 3-5 段说明各部分讲了什么、得出了什么结论。\r\n" +
                "只写正文，不要开场白，不要用 Markdown 的 # 标题。"),

            new PageMode("bullets", "要点清单", "6~10 条带数字的要点", 1800,
                "请把下面这篇网页压缩成 6-10 条要点，每条一行，用「- 」开头。\r\n" +
                "每条必须承载具体信息（结论、数字、名称、时间），不要写「介绍了…」这种空话，不要评论，不要重复。"),

            new PageMode("outline", "结构大纲", "层级目录，看清骨架", 1800,
                "请为下面这篇网页生成结构大纲，按原文顺序还原作者的论证骨架：\r\n" +
                "一级用「# 」，二级用「- 」，每层一行、不超过 20 字，保留关键术语。\r\n" +
                "只输出大纲，不要解释。"),

            new PageMode("tldr", "一句话", "40 字说清这页在讲什么", 200,
                "用一句不超过 40 字的中文说清下面这篇网页在讲什么、结论是什么。只输出这一句，不要任何前缀。"),

            new PageMode("deep", "深度解读", "主张 / 论证 / 前提 / 盲点", 3000,
                "请深度解读下面这篇网页，严格按四块输出：\r\n" +
                "## 核心主张\r\n（作者到底想说什么，用一两句）\r\n" +
                "## 论证链条\r\n（支撑它的关键理由，按原文顺序列出）\r\n" +
                "## 隐含前提\r\n（作者默认成立、但没有论证的东西）\r\n" +
                "## 值得警惕\r\n（证据不足、立场偏差、遗漏的反面）\r\n" +
                "只写这四块，不要开场白。"),

            new PageMode("data", "关键数据", "数字 / 时间 / 金额 / 指标", 1600,
                "请从下面这篇网页里抽取所有关键数据，用「- 指标：数值 —— 上下文一句话」的列表给出。\r\n" +
                "数字必须与原文一致，原文没有的绝对不要补。\r\n" +
                "如果全文确实没有具体数字，就回一句「本文没有给出具体数据」，再列 3 条关键事实。"),

            new PageMode("action", "行动清单", "看完能做什么", 1600,
                "请把下面这篇网页转成可执行的行动清单：用「1. 」编号，每条以动词开头，写清做什么、为什么值得做、大致要多久。\r\n" +
                "如果文章本身没有可执行内容，就给出「看完这页之后值得做的 3 件事」。不要复述文章，只给动作。"),

            new PageMode("questions", "追问清单", "接下来该问什么", 1800,
                "请针对下面这篇网页列出 6-8 个值得继续追问的问题，用「- 问题 —— 为什么会这么问」的形式。\r\n" +
                "从最基础的问题排到最深入的问题，优先覆盖文章含糊、缺少证据、或结论跳跃的地方。"),

            new PageMode("terms", "术语表", "关键概念一句话解释", 1800,
                "请从下面这篇网页里挑出 5-10 个读者可能不熟的术语、人名、产品或缩写，用「- 术语：一句话解释」的形式给出。\r\n" +
                "解释要贴着本文语境，写清它在这篇文章里指什么，不要百科式泛谈。")
        };

        /// <summary>
        /// 内置方案表（顺序即 UI 顺序）。现在真正给 UI 用的是 PromptLib —— 它会用这张表播种，
        /// 之后用户在面板上改了 / 加了 / 删了都写在 prompts.json 里，这里只当出厂默认值。
        /// </summary>
        public static PageMode[] BuiltinTable()
        {
            PageMode[] copy = new PageMode[Table.Length];
            Array.Copy(Table, copy, Table.Length);
            return copy;
        }

        /// <summary>全部内置方案（顺序即 UI 顺序）。</summary>
        public static PageMode[] All()
        {
            return BuiltinTable();
        }

        public static int Count()
        {
            return Table.Length;
        }

        public static PageMode ByIndex(int i)
        {
            if (i < 0 || i >= Table.Length) return Table[0];
            return Table[i];
        }

        /// <summary>按 id 取方案；空 / 未知 id 一律回到默认方案（简明总结）。</summary>
        public static PageMode Get(string id)
        {
            if (id != null && id.Trim().Length > 0)
            {
                string want = id.Trim().ToLowerInvariant();
                for (int i = 0; i < Table.Length; i++)
                {
                    if (Table[i].Id == want) return Table[i];
                }
            }
            return Table[0];
        }

        public static bool IsValid(string id)
        {
            if (id == null) return false;
            string want = id.Trim().ToLowerInvariant();
            for (int i = 0; i < Table.Length; i++)
            {
                if (Table[i].Id == want) return true;
            }
            return false;
        }

        /// <summary>
        /// 按 max_tokens 估算的合理超时：20s 起步 + 每 token 40ms（推理模型出字慢）。
        /// 深度解读这类长方案动辄一分钟以上，用设置里的 30s 默认超时必然失败。
        /// </summary>
        public static int TimeoutFor(PageMode m)
        {
            if (m == null) return 60000;
            int t = 20000 + m.MaxTokens * 40;
            if (t < 30000) t = 30000;
            if (t > 180000) t = 180000;
            return t;
        }

        /// <summary>给 JSON / 日志用的 id 列表。</summary>
        public static string IdCsv()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < Table.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Table[i].Id);
            }
            return sb.ToString();
        }
    }
}
