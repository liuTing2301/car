using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DigitalTwin.Scripting
{
    /// <summary>脚本编译 / 运行期错误（带行号）。</summary>
    public class ScriptException : Exception
    {
        public readonly int Line;
        public ScriptException(string message, int line) : base(line > 0 ? $"第 {line} 行：{message}" : message)
        {
            Line = line;
        }
    }

    // ============================= 词法 =============================
    internal enum Tok
    {
        Number, String, Ident, True, False,
        Plus, Minus, Star, Slash, Percent,
        Assign, PlusAssign, MinusAssign, StarAssign, SlashAssign,
        Eq, Ne, Lt, Le, Gt, Ge, AndAnd, OrOr, Not,
        LParen, RParen, LBrace, RBrace, Comma, Semi,
        If, Else, While, For, Break, Continue,
        Eof
    }

    internal sealed class Token
    {
        public Tok type;
        public string text;
        public double num;
        public int line;
        public Token(Tok t, string txt, int ln) { type = t; text = txt; line = ln; }
    }

    internal sealed class Lexer
    {
        private readonly string s;
        private int i;
        private int line = 1;

        public Lexer(string src) { s = src ?? ""; }

        private char Cur => i < s.Length ? s[i] : '\0';

        public List<Token> Tokenize()
        {
            List<Token> list = new List<Token>();
            while (true)
            {
                Token t = Next();
                list.Add(t);
                if (t.type == Tok.Eof) break;
            }
            return list;
        }

        private Token Next()
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\n') { line++; i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/'))
                    {
                        if (s[i] == '\n') line++;
                        i++;
                    }
                    i = Math.Min(s.Length, i + 2);
                    continue;
                }
                break;
            }

            if (i >= s.Length) return new Token(Tok.Eof, "", line);

            int startLine = line;
            char ch = s[i];

            if (char.IsDigit(ch) || (ch == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                string txt = s.Substring(start, i - start);
                double v = double.Parse(txt, CultureInfo.InvariantCulture);
                return new Token(Tok.Number, txt, startLine) { num = v };
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                string word = s.Substring(start, i - start);
                switch (word)
                {
                    case "true": return new Token(Tok.True, word, startLine);
                    case "false": return new Token(Tok.False, word, startLine);
                    case "if": return new Token(Tok.If, word, startLine);
                    case "else": return new Token(Tok.Else, word, startLine);
                    case "while": return new Token(Tok.While, word, startLine);
                    case "for": return new Token(Tok.For, word, startLine);
                    case "break": return new Token(Tok.Break, word, startLine);
                    case "continue": return new Token(Tok.Continue, word, startLine);
                    default: return new Token(Tok.Ident, word, startLine);
                }
            }

            if (ch == '"' || ch == '\'')
            {
                char quote = ch;
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < s.Length && s[i] != quote)
                {
                    char c = s[i];
                    if (c == '\n') line++;
                    if (c == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        char e = s[i];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            case '\'': sb.Append('\''); break;
                            default: sb.Append(e); break;
                        }
                        i++;
                        continue;
                    }
                    sb.Append(c);
                    i++;
                }
                if (i >= s.Length) throw new ScriptException("字符串未闭合", startLine);
                i++;
                return new Token(Tok.String, sb.ToString(), startLine);
            }

            i++;
            switch (ch)
            {
                case '+': if (Cur == '=') { i++; return new Token(Tok.PlusAssign, "+=", startLine); } return new Token(Tok.Plus, "+", startLine);
                case '-': if (Cur == '=') { i++; return new Token(Tok.MinusAssign, "-=", startLine); } return new Token(Tok.Minus, "-", startLine);
                case '*': if (Cur == '=') { i++; return new Token(Tok.StarAssign, "*=", startLine); } return new Token(Tok.Star, "*", startLine);
                case '/': if (Cur == '=') { i++; return new Token(Tok.SlashAssign, "/=", startLine); } return new Token(Tok.Slash, "/", startLine);
                case '%': return new Token(Tok.Percent, "%", startLine);
                case '=': if (Cur == '=') { i++; return new Token(Tok.Eq, "==", startLine); } return new Token(Tok.Assign, "=", startLine);
                case '!': if (Cur == '=') { i++; return new Token(Tok.Ne, "!=", startLine); } return new Token(Tok.Not, "!", startLine);
                case '<': if (Cur == '=') { i++; return new Token(Tok.Le, "<=", startLine); } return new Token(Tok.Lt, "<", startLine);
                case '>': if (Cur == '=') { i++; return new Token(Tok.Ge, ">=", startLine); } return new Token(Tok.Gt, ">", startLine);
                case '&': if (Cur == '&') { i++; return new Token(Tok.AndAnd, "&&", startLine); } break;
                case '|': if (Cur == '|') { i++; return new Token(Tok.OrOr, "||", startLine); } break;
                case '(': return new Token(Tok.LParen, "(", startLine);
                case ')': return new Token(Tok.RParen, ")", startLine);
                case '{': return new Token(Tok.LBrace, "{", startLine);
                case '}': return new Token(Tok.RBrace, "}", startLine);
                case ',': return new Token(Tok.Comma, ",", startLine);
                case ';': return new Token(Tok.Semi, ";", startLine);
            }
            throw new ScriptException($"无法识别的字符 '{ch}'", startLine);
        }
    }

    // ============================= 值工具 =============================
    internal static class V
    {
        public static double Num(object o, int line)
        {
            if (o is double d) return d;
            if (o is bool b) return b ? 1d : 0d;
            if (o is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double r)) return r;
            throw new ScriptException($"期望数字，实际是{TypeName(o)}", line);
        }

        public static bool Bool(object o)
        {
            if (o is bool b) return b;
            if (o is double d) return Math.Abs(d) > 1e-12;
            if (o is string s) return s.Length > 0;
            return false;
        }

        public static string Str(object o)
        {
            if (o == null) return "";
            if (o is double d) return d.ToString("0.####", CultureInfo.InvariantCulture);
            if (o is bool b) return b ? "true" : "false";
            return o.ToString();
        }

        public static bool Equal(object a, object b)
        {
            if (a is string || b is string) return Str(a) == Str(b);
            if (a is bool && b is bool) return (bool)a == (bool)b;
            if (a is double && b is double) return Math.Abs((double)a - (double)b) < 1e-9;
            return Str(a) == Str(b);
        }

        public static int Cmp(object a, object b, int line)
        {
            if (a is string || b is string) return string.CompareOrdinal(Str(a), Str(b));
            return Num(a, line).CompareTo(Num(b, line));
        }

        public static string TypeName(object o)
        {
            if (o is double) return "数字";
            if (o is string) return "字符串";
            if (o is bool) return "布尔";
            return "空";
        }

        public static double Clamp(double v, double a, double b) => v < a ? a : (v > b ? b : v);
    }

    // ============================= 语法树 =============================
    internal abstract class Stmt { public abstract void Exec(Interp it); }

    internal sealed class Block : Stmt
    {
        public List<Stmt> body;
        public override void Exec(Interp it) { for (int k = 0; k < body.Count; k++) body[k].Exec(it); }
    }

    internal sealed class EmptyStmt : Stmt { public override void Exec(Interp it) { } }
    internal sealed class BreakStmt : Stmt { public override void Exec(Interp it) { throw new BreakSignal(); } }
    internal sealed class ContinueStmt : Stmt { public override void Exec(Interp it) { throw new ContinueSignal(); } }

    internal sealed class ExprStmt : Stmt
    {
        public Expr e;
        public override void Exec(Interp it) { e.Eval(it); }
    }

    internal sealed class AssignStmt : Stmt
    {
        public string name;
        public Tok op;
        public Expr value;
        public int line;
        public override void Exec(Interp it)
        {
            object v = value.Eval(it);
            if (op == Tok.Assign) { it.SetVar(name, v, line); return; }
            double a = V.Num(it.GetVar(name, line), line);
            double b = V.Num(v, line);
            double r = op == Tok.PlusAssign ? a + b : op == Tok.MinusAssign ? a - b : op == Tok.StarAssign ? a * b : a / b;
            it.SetVar(name, r, line);
        }
    }

    internal sealed class IfStmt : Stmt
    {
        public Expr cond;
        public Stmt then;
        public Stmt els;
        public override void Exec(Interp it)
        {
            if (V.Bool(cond.Eval(it))) then.Exec(it);
            else els?.Exec(it);
        }
    }

    internal sealed class WhileStmt : Stmt
    {
        public Expr cond;
        public Stmt body;
        public int line;
        public override void Exec(Interp it)
        {
            while (true)
            {
                it.Step(line);
                if (!V.Bool(cond.Eval(it))) break;
                try { body.Exec(it); }
                catch (BreakSignal) { break; }
                catch (ContinueSignal) { continue; }
            }
        }
    }

    internal sealed class ForStmt : Stmt
    {
        public Stmt init;
        public Expr cond;
        public Stmt update;
        public Stmt body;
        public int line;
        public override void Exec(Interp it)
        {
            if (init != null) init.Exec(it);
            while (true)
            {
                it.Step(line);
                if (cond != null && !V.Bool(cond.Eval(it))) break;
                try { body.Exec(it); }
                catch (BreakSignal) { break; }
                catch (ContinueSignal) { }
                if (update != null) update.Exec(it);
            }
        }
    }

    internal abstract class Expr { public abstract object Eval(Interp it); }

    internal sealed class NumExpr : Expr
    {
        public double v;
        public override object Eval(Interp it) { it.Step(0); return v; }
    }

    internal sealed class StrExpr : Expr
    {
        public string v;
        public override object Eval(Interp it) { it.Step(0); return v; }
    }

    internal sealed class BoolExpr : Expr
    {
        public bool v;
        public override object Eval(Interp it) { it.Step(0); return v; }
    }

    internal sealed class VarExpr : Expr
    {
        public string name;
        public int line;
        public override object Eval(Interp it) { return it.GetVar(name, line); }
    }

    internal sealed class UnaryExpr : Expr
    {
        public Tok op;
        public Expr e;
        public int line;
        public override object Eval(Interp it)
        {
            object v = e.Eval(it);
            if (op == Tok.Minus) return -V.Num(v, line);
            return !V.Bool(v);
        }
    }

    internal sealed class BinExpr : Expr
    {
        public Tok op;
        public Expr l;
        public Expr r;
        public int line;
        public override object Eval(Interp it)
        {
            object a = l.Eval(it);
            object b = r.Eval(it);
            switch (op)
            {
                case Tok.Plus:
                    if (a is string || b is string) return V.Str(a) + V.Str(b);
                    return V.Num(a, line) + V.Num(b, line);
                case Tok.Minus: return V.Num(a, line) - V.Num(b, line);
                case Tok.Star: return V.Num(a, line) * V.Num(b, line);
                case Tok.Slash:
                    {
                        double d = V.Num(b, line);
                        if (Math.Abs(d) < 1e-12) throw new ScriptException("除数为 0", line);
                        return V.Num(a, line) / d;
                    }
                case Tok.Percent:
                    {
                        double d = V.Num(b, line);
                        if (Math.Abs(d) < 1e-12) throw new ScriptException("除数为 0", line);
                        return V.Num(a, line) % d;
                    }
                case Tok.Eq: return V.Equal(a, b);
                case Tok.Ne: return !V.Equal(a, b);
                case Tok.Lt: return V.Cmp(a, b, line) < 0;
                case Tok.Le: return V.Cmp(a, b, line) <= 0;
                case Tok.Gt: return V.Cmp(a, b, line) > 0;
                case Tok.Ge: return V.Cmp(a, b, line) >= 0;
            }
            throw new ScriptException("未知运算符", line);
        }
    }

    internal sealed class LogicExpr : Expr
    {
        public Tok op;
        public Expr l;
        public Expr r;
        public int line;
        public override object Eval(Interp it)
        {
            bool a = V.Bool(l.Eval(it));
            if (op == Tok.AndAnd)
            {
                if (!a) return false;
                return V.Bool(r.Eval(it));
            }
            if (a) return true;
            return V.Bool(r.Eval(it));
        }
    }

    internal sealed class CallExpr : Expr
    {
        public string name;
        public List<Expr> args;
        public int line;
        public override object Eval(Interp it)
        {
            List<object> vals = new List<object>(args.Count);
            for (int k = 0; k < args.Count; k++) vals.Add(args[k].Eval(it));
            return it.Call(name, vals, line);
        }
    }

    // ============================= 语法分析 =============================
    internal sealed class Parser
    {
        private readonly List<Token> t;
        private int p;

        public Parser(List<Token> tokens) { t = tokens; }

        private Token Cur => t[p];
        private Token Advance() { if (p < t.Count - 1) p++; return t[p - 1]; }
        private bool Check(Tok k) => t[p].type == k;
        private bool Match(params Tok[] ks)
        {
            foreach (Tok k in ks)
            {
                if (Check(k)) { Advance(); return true; }
            }
            return false;
        }
        private void Expect(Tok k, string what)
        {
            if (!Check(k)) throw new ScriptException($"期望 {what}，但遇到 '{t[p].text}'", t[p].line);
            Advance();
        }

        public Stmt ParseProgram()
        {
            List<Stmt> list = new List<Stmt>();
            while (!Check(Tok.Eof))
            {
                if (Match(Tok.Semi)) continue;
                list.Add(ParseStatement());
            }
            return new Block { body = list };
        }

        private Stmt ParseStatement()
        {
            if (Check(Tok.LBrace)) return ParseBlock();
            if (Match(Tok.If)) return ParseIf();
            if (Match(Tok.While)) return ParseWhile();
            if (Match(Tok.For)) return ParseFor();
            if (Match(Tok.Break)) { Match(Tok.Semi); return new BreakStmt(); }
            if (Match(Tok.Continue)) { Match(Tok.Semi); return new ContinueStmt(); }
            if (Match(Tok.Semi)) return new EmptyStmt();
            return ParseSimple(true);
        }

        private Stmt ParseBlock()
        {
            Expect(Tok.LBrace, "'{'");
            List<Stmt> list = new List<Stmt>();
            while (!Check(Tok.RBrace) && !Check(Tok.Eof))
            {
                if (Match(Tok.Semi)) continue;
                list.Add(ParseStatement());
            }
            Expect(Tok.RBrace, "'}'");
            return new Block { body = list };
        }

        private Stmt ParseIf()
        {
            Expect(Tok.LParen, "'('");
            Expr cond = ParseExpr();
            Expect(Tok.RParen, "')'");
            Stmt then = ParseStatement();
            Stmt els = null;
            if (Match(Tok.Else)) els = ParseStatement();
            return new IfStmt { cond = cond, then = then, els = els };
        }

        private Stmt ParseWhile()
        {
            int line = t[p - 1].line;
            Expect(Tok.LParen, "'('");
            Expr cond = ParseExpr();
            Expect(Tok.RParen, "')'");
            Stmt body = ParseStatement();
            return new WhileStmt { cond = cond, body = body, line = line };
        }

        private Stmt ParseFor()
        {
            int line = t[p - 1].line;
            Expect(Tok.LParen, "'('");
            Stmt init = Check(Tok.Semi) ? null : ParseSimple(false);
            Expect(Tok.Semi, "';'");
            Expr cond = Check(Tok.Semi) ? null : ParseExpr();
            Expect(Tok.Semi, "';'");
            Stmt update = Check(Tok.RParen) ? null : ParseSimple(false);
            Expect(Tok.RParen, "')'");
            Stmt body = ParseStatement();
            return new ForStmt { init = init, cond = cond, update = update, body = body, line = line };
        }

        private static bool IsAssignOp(Tok k) =>
            k == Tok.Assign || k == Tok.PlusAssign || k == Tok.MinusAssign || k == Tok.StarAssign || k == Tok.SlashAssign;

        private Stmt ParseSimple(bool consumeSemi)
        {
            if (Check(Tok.Ident) && IsAssignOp(t[p + 1].type))
            {
                Token nameTok = Advance();
                Token opTok = Advance();
                Expr val = ParseExpr();
                if (consumeSemi) Match(Tok.Semi);
                return new AssignStmt { name = nameTok.text, op = opTok.type, value = val, line = nameTok.line };
            }
            Expr e = ParseExpr();
            if (consumeSemi) Match(Tok.Semi);
            return new ExprStmt { e = e };
        }

        private Expr ParseExpr() => ParseOr();

        private Expr ParseOr()
        {
            Expr l = ParseAnd();
            while (Match(Tok.OrOr))
            {
                int line = t[p - 1].line;
                Expr r = ParseAnd();
                l = new LogicExpr { op = Tok.OrOr, l = l, r = r, line = line };
            }
            return l;
        }

        private Expr ParseAnd()
        {
            Expr l = ParseEquality();
            while (Match(Tok.AndAnd))
            {
                int line = t[p - 1].line;
                Expr r = ParseEquality();
                l = new LogicExpr { op = Tok.AndAnd, l = l, r = r, line = line };
            }
            return l;
        }

        private Expr ParseEquality()
        {
            Expr l = ParseRelational();
            while (Check(Tok.Eq) || Check(Tok.Ne))
            {
                Tok op = Advance().type;
                int line = t[p - 1].line;
                Expr r = ParseRelational();
                l = new BinExpr { op = op, l = l, r = r, line = line };
            }
            return l;
        }

        private Expr ParseRelational()
        {
            Expr l = ParseAdditive();
            while (Check(Tok.Lt) || Check(Tok.Le) || Check(Tok.Gt) || Check(Tok.Ge))
            {
                Tok op = Advance().type;
                int line = t[p - 1].line;
                Expr r = ParseAdditive();
                l = new BinExpr { op = op, l = l, r = r, line = line };
            }
            return l;
        }

        private Expr ParseAdditive()
        {
            Expr l = ParseMultiplicative();
            while (Check(Tok.Plus) || Check(Tok.Minus))
            {
                Tok op = Advance().type;
                int line = t[p - 1].line;
                Expr r = ParseMultiplicative();
                l = new BinExpr { op = op, l = l, r = r, line = line };
            }
            return l;
        }

        private Expr ParseMultiplicative()
        {
            Expr l = ParseUnary();
            while (Check(Tok.Star) || Check(Tok.Slash) || Check(Tok.Percent))
            {
                Tok op = Advance().type;
                int line = t[p - 1].line;
                Expr r = ParseUnary();
                l = new BinExpr { op = op, l = l, r = r, line = line };
            }
            return l;
        }

        private Expr ParseUnary()
        {
            if (Match(Tok.Minus))
            {
                int line = t[p - 1].line;
                return new UnaryExpr { op = Tok.Minus, e = ParseUnary(), line = line };
            }
            if (Match(Tok.Not))
            {
                int line = t[p - 1].line;
                return new UnaryExpr { op = Tok.Not, e = ParseUnary(), line = line };
            }
            if (Match(Tok.Plus)) return ParseUnary();
            return ParsePrimary();
        }

        private Expr ParsePrimary()
        {
            if (Check(Tok.Number)) { Token tk = Advance(); return new NumExpr { v = tk.num }; }
            if (Check(Tok.String)) { Token tk = Advance(); return new StrExpr { v = tk.text }; }
            if (Match(Tok.True)) return new BoolExpr { v = true };
            if (Match(Tok.False)) return new BoolExpr { v = false };
            if (Check(Tok.Ident))
            {
                Token tk = Advance();
                if (Match(Tok.LParen))
                {
                    List<Expr> args = new List<Expr>();
                    if (!Check(Tok.RParen))
                    {
                        do { args.Add(ParseExpr()); } while (Match(Tok.Comma));
                    }
                    Expect(Tok.RParen, "')'");
                    return new CallExpr { name = tk.text, args = args, line = tk.line };
                }
                return new VarExpr { name = tk.text, line = tk.line };
            }
            if (Match(Tok.LParen))
            {
                Expr e = ParseExpr();
                Expect(Tok.RParen, "')'");
                return e;
            }
            throw new ScriptException($"意外的符号 '{t[p].text}'", t[p].line);
        }
    }

    // ============================= 运行时 =============================
    internal sealed class BreakSignal : Exception { }
    internal sealed class ContinueSignal : Exception { }

    internal sealed class Interp
    {
        public ScriptContext ctx;
        public Dictionary<string, object> state;
        public int steps;
        public int stepLimit;

        public void Step(int line)
        {
            if (++steps > stepLimit)
                throw new ScriptException("脚本执行步数超限（疑似死循环），本轮已中止", line);
        }

        public object GetVar(string name, int line)
        {
            Step(line);
            if (ctx != null && ctx.TryGet(name, out object v)) return v;
            if (state.TryGetValue(name, out object sv)) return sv;
            throw new ScriptException($"未定义变量 '{name}'", line);
        }

        public void SetVar(string name, object value, int line)
        {
            Step(line);
            if (ctx != null)
            {
                if (ctx.IsWritable(name)) { ctx.Set(name, value); return; }
                if (ctx.IsReadOnly(name)) throw new ScriptException($"'{name}' 是只读数据，不可修改", line);
            }
            state[name] = value;
        }

        public object Call(string name, List<object> args, int line)
        {
            Step(line);
            return ctx.CallBuiltin(name, args, line);
        }
    }

    /// <summary>编译后的脚本。</summary>
    public sealed class ScriptProgram
    {
        internal Stmt root;
        public string source;
    }

    /// <summary>脚本语言引擎：编译 + 执行（纯托管解释执行，跨平台、可沙箱）。</summary>
    public static class ScriptEngine
    {
        public static ScriptProgram Compile(string source)
        {
            string src = source ?? "";
            List<Token> tokens = new Lexer(src).Tokenize();
            Stmt root = new Parser(tokens).ParseProgram();
            return new ScriptProgram { root = root, source = src };
        }

        /// <summary>执行一遍脚本。throw ScriptException 表示编译期/运行期错误。</summary>
        public static void Execute(ScriptProgram program, ScriptContext ctx, Dictionary<string, object> state, int stepLimit)
        {
            if (program == null) return;
            Interp it = new Interp { ctx = ctx, state = state, stepLimit = stepLimit };
            try { program.root.Exec(it); }
            catch (BreakSignal) { }
            catch (ContinueSignal) { }
        }
    }

    /// <summary>脚本可访问的变量 / 函数绑定：读写的本地变量、只读的接收数据、内置函数。</summary>
    public sealed class ScriptContext
    {
        private readonly Dictionary<string, Func<object>> getters = new Dictionary<string, Func<object>>();
        private readonly Dictionary<string, Action<object>> setters = new Dictionary<string, Action<object>>();
        private readonly Dictionary<string, Func<List<object>, object>> builtins = new Dictionary<string, Func<List<object>, object>>();
        private static readonly Random rng = new Random();
        private const int MaxLog = 100;

        /// <summary>print() 输出（每次执行前由运行器清空）。</summary>
        public readonly List<string> Log = new List<string>();

        public ScriptContext() { RegisterBuiltins(); }

        public void Bind(string name, Func<object> get, Action<object> set)
        {
            if (get != null) getters[name] = get;
            if (set != null) setters[name] = set;
        }

        public void BindConst(string name, object value) => getters[name] = () => value;

        public bool TryGet(string name, out object value)
        {
            if (getters.TryGetValue(name, out Func<object> g)) { value = g(); return true; }
            value = null;
            return false;
        }

        public bool IsWritable(string name) => setters.ContainsKey(name);
        public bool IsReadOnly(string name) => getters.ContainsKey(name) && !setters.ContainsKey(name);
        public bool Exists(string name) => getters.ContainsKey(name) || setters.ContainsKey(name);

        public void Set(string name, object value)
        {
            if (setters.TryGetValue(name, out Action<object> s)) s(value);
        }

        public object CallBuiltin(string name, List<object> args, int line)
        {
            if (builtins.TryGetValue(name, out Func<List<object>, object> fn)) return fn(args);
            throw new ScriptException($"未知函数 '{name}'", line);
        }

        private void RegisterBuiltins()
        {
            BindBuiltin("print", a => { Push(string.Join(" ", a.ConvertAll(V.Str))); return null; });
            BindBuiltin("log", a => { Push(string.Join(" ", a.ConvertAll(V.Str))); return null; });
            BindBuiltin("clamp", a => V.Clamp(Arg(a, 0), Arg(a, 1), Arg(a, 2)));
            BindBuiltin("min", a => Math.Min(Arg(a, 0), Arg(a, 1)));
            BindBuiltin("max", a => Math.Max(Arg(a, 0), Arg(a, 1)));
            BindBuiltin("abs", a => Math.Abs(Arg(a, 0)));
            BindBuiltin("round", a => Math.Round(Arg(a, 0)));
            BindBuiltin("floor", a => Math.Floor(Arg(a, 0)));
            BindBuiltin("ceil", a => Math.Ceiling(Arg(a, 0)));
            BindBuiltin("sqrt", a => Math.Sqrt(Math.Max(0d, Arg(a, 0))));
            BindBuiltin("pow", a => Math.Pow(Arg(a, 0), Arg(a, 1)));
            BindBuiltin("sin", a => Math.Sin(Arg(a, 0)));
            BindBuiltin("cos", a => Math.Cos(Arg(a, 0)));
            BindBuiltin("tan", a => Math.Tan(Arg(a, 0)));
            BindBuiltin("atan2", a => Math.Atan2(Arg(a, 0), Arg(a, 1)));
            BindBuiltin("sign", a => (double)Math.Sign(Arg(a, 0)));
            BindBuiltin("lerp", a => { double t = V.Clamp(Arg(a, 2), 0d, 1d); return Arg(a, 0) + (Arg(a, 1) - Arg(a, 0)) * t; });
            BindBuiltin("deg", a => Arg(a, 0) * 180d / Math.PI);
            BindBuiltin("rad", a => Arg(a, 0) * Math.PI / 180d);
            BindBuiltin("random", a => rng.NextDouble());
            BindBuiltin("randint", a => (double)rng.Next((int)Arg(a, 0), (int)Arg(a, 1) + 1));
        }

        private void BindBuiltin(string name, Func<List<object>, object> fn) => builtins[name] = fn;

        private static double Arg(List<object> a, int i) => i < a.Count ? V.Num(a[i], 0) : 0d;

        private void Push(string s)
        {
            Log.Add(s);
            if (Log.Count > MaxLog) Log.RemoveAt(0);
        }
    }
}
