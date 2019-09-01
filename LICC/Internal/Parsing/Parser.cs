﻿using LICC.Internal.Parsing.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LICC.Internal.Parsing
{
    internal class Parser
    {
        private int Index;
        private Lexeme Current => Lexemes[Index];
        private SourceLocation Location => Current.Begin;

        private Lexeme[] Lexemes;
        private Stack<int> IndexStack = new Stack<int>();

        #region Utils
        private void Advance()
        {
            Index++;

            if (Index >= Lexemes.Length)
                Index = Lexemes.Length - 1;
        }

        private void AdvanceUntil(LexemeKind kind)
        {
            while (Current.Kind != kind)
                Advance();
        }

        private void Back()
        {
            Index--;
        }

        private void Push() => IndexStack.Push(Index);

        private void Pop(bool set = true)
        {
            int i = IndexStack.Pop();
            if (set)
                Index = i;
        }

        private void SkipWhitespaces(bool alsoNewlines = true)
        {
            while (Current.Kind == LexemeKind.Whitespace || (Current.Kind == LexemeKind.NewLine && alsoNewlines))
                Advance();
        }

        private Lexeme TakeAny()
        {
            var token = Current;
            Advance();
            return token;
        }

        private Lexeme Take(LexemeKind lexemeKind, string expected = null, bool ignoreWhitespace = true)
        {
            if (ignoreWhitespace)
                SkipWhitespaces();

            if (Current.Kind != lexemeKind)
                Error($"Unexpected lexeme: expected {expected ?? lexemeKind.ToString()}, found {Current.Kind}");

            return TakeAny();
        }

        private bool Take(LexemeKind lexemeKind, out Lexeme lexeme, bool ignoreWhitespace = true)
        {
            Push();

            if (ignoreWhitespace)
                SkipWhitespaces();

            if (Current.Kind == lexemeKind)
            {
                lexeme = TakeAny();
                Pop(false);
                return true;
            }

            lexeme = null;
            Pop();
            return false;
        }

        private Lexeme TakeKeyword(string keyword, bool ignoreWhitespace = true, bool @throw = true, string msg = null)
        {
            if (ignoreWhitespace)
                SkipWhitespaces();

            if (Current.Kind != LexemeKind.Keyword || Current.Content != keyword)
            {
                if (@throw)
                    Error(msg ?? $"Unexpected lexeme: expected '{keyword}' keyword, found {Current}");
                else
                    return null;
            }

            return TakeAny();
        }

        private void Error(string msg) => Error(msg, Location);
        private void Error(string msg, SourceLocation location)
        {
            throw new ParseException(new Error(location, msg, Severity.Error));
        }
        #endregion

        public File ParseFile(Lexeme[] lexemes)
        {
            Index = 0;
            Lexemes = lexemes;

            var statements = new List<IStatement>();

            while (Current.Kind != LexemeKind.EndOfFile)
            {
                SkipWhitespaces();

                var st = GetStatement();

                if (!(st is CommentStatement))
                    statements.Add(st);
            }

            return new File(statements);
        }

        private IStatement GetStatement()
        {
            switch (Current.Kind)
            {
                case LexemeKind.Keyword:
                    return DoFunction();
                case LexemeKind.String:
                    SkipWhitespaces();

                    if (Current.Kind != LexemeKind.String && Current.Kind != LexemeKind.QuotedString)
                        return null;

                    return DoCommand();
                case LexemeKind.Hashtag:
                    AdvanceUntil(LexemeKind.NewLine);
                    return new CommentStatement();
                case LexemeKind.Exclamation:
                    Advance();
                    return new ExpressionStatement(DoFunctionCall());
            }

            return null;
        }

        private FunctionDeclarationStatement DoFunction()
        {
            TakeKeyword("function");

            string name = Take(LexemeKind.String, "function name").Content;
            //Take(LexemeKind.Whitespace, "whitespace after function name", false);

            var statements = new List<IStatement>();
            var parameters = new List<Parameter>();

            Take(LexemeKind.LeftParenthesis, "parameter list opening");

            while (true)
            {
                SkipWhitespaces();
                if (Current.Kind != LexemeKind.String)
                    break;

                parameters.Add(DoParameter());

                SkipWhitespaces();
                if (Current.Kind != LexemeKind.Comma)
                    break;
                else
                    Advance();
            }

            Take(LexemeKind.RightParenthesis, "parameter list closing");
            SkipWhitespaces();

            Take(LexemeKind.LeftBrace, "function body opening");
            SkipWhitespaces();

            IStatement statement;
            while ((statement = GetStatement()) != null)
            {
                if (statement is FunctionDeclarationStatement)
                    Error("cannot declare function inside function");

                if (!(statement is CommentStatement))
                    statements.Add(statement);
            }

            Take(LexemeKind.RightBrace, "function body closing");

            return new FunctionDeclarationStatement(name, statements, parameters);
        }

        private Parameter DoParameter()
        {
            string type = Take(LexemeKind.String, "parameter type").Content;
            string name = Take(LexemeKind.String, "parameter name").Content;

            return new Parameter(type, name);
        }

        private CommandStatement DoCommand()
        {
            string cmdName = Take(LexemeKind.String).Content;
            var args = DoArguments();

            if (Current.Kind == LexemeKind.Semicolon)
                Advance();
            else if (Current.Kind == LexemeKind.Hashtag)
                AdvanceUntil(LexemeKind.NewLine);

            return new CommandStatement(cmdName, args.ToArray());
        }

        private IEnumerable<Expression> DoArguments()
        {
            var args = new List<Expression>();

            Expression expr;
            while (Current.Kind != LexemeKind.NewLine && (expr = DoExpression()) != null)
                args.Add(expr);

            return args;
        }

        private Expression DoExpression(bool doOperator = true)
        {
            Expression ret = null;

            if (Take(LexemeKind.LeftParenthesis, out _))
            {
                ret = DoExpression();

                Take(LexemeKind.RightParenthesis, "closing parentheses");
            }
            else if (Take(LexemeKind.String, out var str))
            {
                if (float.TryParse(str.Content, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    ret = new NumberLiteralExpression(f);
                else if (str.Content.Length > 1 && str.Content[0] == '$')
                    ret = new VariableAccessExpression(str.Content.Substring(1));
                else
                    Error($"invalid string '{str.Content}'");
            }
            else if (Take(LexemeKind.QuotedString, out var quotedStr))
            {
                ret = new StringLiteralExpression(quotedStr.Content);
            }
            else if (Take(LexemeKind.Exclamation, out _))
            {
                ret = DoFunctionCall();
            }
            else if (Take(LexemeKind.Keyword, out var keyword))
            {
                if (keyword.Content != "true" && keyword.Content != "false")
                    Error($"unexpected keyword: '{keyword.Content}'");

                ret = new BooleanLiteralExpression(keyword.Content == "true");
            }

            if (ret != null && doOperator)
            {
                return DoOperatorChain(ret);
            }

            return ret;
        }

        private Expression DoOperatorChain(Expression first)
        {
            var items = new List<object>();
            Operator? op;

            do
            {
                op = null;

                if (Take(LexemeKind.Plus, out _))
                    op = Operator.Add;
                else if (Take(LexemeKind.Minus, out _))
                    op = Operator.Subtract;
                else if (Take(LexemeKind.Multiply, out _))
                    op = Operator.Multiply;
                else if (Take(LexemeKind.Divide, out _))
                    op = Operator.Divide;

                if (op != null)
                {
                    items.Add(op.Value);
                    items.Add(DoExpression(false));
                }

            } while (op != null);

            //2 + 4 * 3 - 1

            if (items.Count > 0)
            {
                items.Insert(0, first);

                for (int i = (int)Operator.Multiply; i >= 0; i--)
                {
                    for (int j = 0; j < items.Count; j++)
                    {
                        var item = items[j];

                        if (item is Operator o && o == (Operator)i)
                        {
                            items[j - 1] = new BinaryOperatorExpression(items[j - 1] as Expression, items[j + 1] as Expression, o);
                            items.RemoveAt(j);
                            items.RemoveAt(j);
                        }
                    }
                }

                if (items.Count > 1)
                    Error("invalid operator chain");

                return items[0] as Expression;
            }
            else
            {
                return first;
            }
        }

        private FunctionCallExpression DoFunctionCall()
        {
            string funcName = Take(LexemeKind.String, "function name", false).Content;
            var args = DoArguments();

            return new FunctionCallExpression(funcName, args.ToArray());
        }
    }
}
