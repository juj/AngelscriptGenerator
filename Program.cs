using System.Xml;
using System.IO;
using System.Web;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocGenerator;

namespace EmbindGenerator
{
    class Program
    {
        static CodeStructure cs = new CodeStructure();
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: AngelscriptGenerator <directory_to_doxygen_xml_docs> [class1 [class2 [class3 ... [classN]]]]");
                return;
            }
            cs.LoadSymbolsFromDirectory(args[0]);

            List<string> knownSymbolNames = new List<string>();
            knownSymbolNames.Add(""); // Typeless 'types', e.g. return value of ctor is parsed to an empty string.
            knownSymbolNames.Add("void");
            knownSymbolNames.Add("bool");
            knownSymbolNames.Add("char");
            knownSymbolNames.Add("signed char");
            knownSymbolNames.Add("unsigned char");
            knownSymbolNames.Add("short");
            knownSymbolNames.Add("signed short");
            knownSymbolNames.Add("unsigned short");
            knownSymbolNames.Add("int");
            knownSymbolNames.Add("signed int");
            knownSymbolNames.Add("unsigned int");
            knownSymbolNames.Add("long");
            knownSymbolNames.Add("signed long");
            knownSymbolNames.Add("unsigned long");
            knownSymbolNames.Add("float");
            knownSymbolNames.Add("double");
            knownSymbolNames.Add("unsigned int");
            knownSymbolNames.Add("std::string");
            for (int i = 1; i < args.Length; ++i)
                knownSymbolNames.Add(args[i]);

            string t =
                "#pragma once\n" +
                "#include <angelscript.h>\n\n";
            tw.Write(t);

            for (int i = 1; i < args.Length; ++i)
                GenerateCtorFunctions(args[i]);

            t = "void RegisterAngelscriptObjects(asIScriptEngine *engine)\n" +
                 "{\n" +
                 "\tint r;\n\n";
            tw.Write(t);

            for (int i = 1; i < args.Length; ++i)
                RegisterObjectType(args[i]);

            for (int i = 1; i < args.Length; ++i)
                GenerateBindingsFile(args[i], knownSymbolNames);

            tw.Write("}\n");

            tw.Flush();
            tw.Close();

            Console.WriteLine("Writing angelscript_symbols_cpp.h done.");
        }

        static TextWriter tw = new StreamWriter("angelscript_symbols_cpp.h");

        static void RegisterObjectType(string className)
        {
            string t = "\tr = engine->RegisterObjectType(\"" + className + "\", sizeof(" + className + "), asOBJ_VALUE | asOBJ_POD | asOBJ_APP_CLASS_C); assert(r >= 0);\n";
            tw.Write(t);
        }

        static void GenerateCtorFunctions(string className)
        {
            if (!cs.symbolsByName.ContainsKey(className))
            {
                Console.WriteLine("Error: Cannot generate bindings for class '" + className + "', XML docs for that class don't exist!");
                return;
            }
            string t = "";

            Symbol s = cs.symbolsByName[className];
            foreach (Symbol f in s.children)
            {
                bool isCtor = f.name == className;
                if (isCtor)
                {
                    bool first = true;
                    string paramList = "";
                    foreach (Parameter p in f.parameters)
                    {
                        if (!first)
                        {
                            paramList += ",";
                        }
                        paramList += p.type;
                        first = false;
                    }

                    string paramListAsIdentifier = paramList.Replace(",", "_").Replace(" ", "_").Replace("&", "ref").Replace("*", "ptr");
                    string args = f.ArgStringWithTypes();
                    args = args.Substring(1, args.Length-2);
                    string args2 = f.ArgStringWithoutTypes();
                    args2 = args2.Substring(1, args2.Length - 2);
                    if (args.Length > 0)
                        args += ", ";
                    t += "static void " + className + "_ctor_" + paramListAsIdentifier + "(" + args + className + " *self)\n";
                    t += "{\n";
                    t += "\tnew(self) " + className + f.ArgStringWithoutTypes() + ";\n";
                    t += "}\n\n";
                }
            }
            tw.Write(t);
        }

        static void GenerateBindingsFile(string className, List<string> knownSymbolNames)
        {
            if (!cs.symbolsByName.ContainsKey(className))
            {
                Console.WriteLine("Error: Cannot generate bindings for class '" + className + "', XML docs for that class don't exist!");
                return;
            }
            string t = "";

            Symbol s = cs.symbolsByName[className];
            foreach(Symbol f in s.children)
            {
                if (f.visibilityLevel != VisibilityLevel.Public)
                    continue; // Only public functions and members are exported.

                List<Symbol> functionOverloads = f.FetchFunctionOverloads(knownSymbolNames);
                bool isGoodSymbol = !f.attributes.Contains("noascript"); // If true, this symbol is exposed. If false, this symbol is not enabled for JS.
                string reason = f.attributes.Contains("noascript") ? "(ignoring since [noascript] specified)" : "";
                if (f.kind == "function")
                {
                    bool isCtor = f.name == className;

                    if (isGoodSymbol && !knownSymbolNames.Contains(f.type))
                    {
                        isGoodSymbol = false;
                        reason += "(" + f.type + " is not known to angelscript)";
                    }

                    string targetFunctionName = f.name; // The JS side name with which this function will be exposed.
                    bool hasOverloads = (functionOverloads.Count > 1);

                    string funcPtrType = f.type + "(" + (f.isStatic ? "" : ( className + "::")) + "*)(";
                    bool first = true;
                    string paramList = "";
                    string paramListForAngelscriptSignature = "";
                    foreach(Parameter p in f.parameters)
                    {
                        if (!first)
                        {
                            paramList += ",";
                            paramListForAngelscriptSignature += ",";
                        }
                        paramList += p.type;
                        paramListForAngelscriptSignature += p.type;
                        if (p.type.EndsWith("&"))
                        {
                            if ((p.type.Contains("const") || p.comment.Contains("[in]")))
                                paramListForAngelscriptSignature += "in";
                            else if (p.comment.Contains("[out]"))
                                paramListForAngelscriptSignature += "out";
                            else
                            {
                                isGoodSymbol = false;
                                reason = "(inout refs are not supported for value types)";
                            }
                        }

                        if (isGoodSymbol && !knownSymbolNames.Contains(p.BasicType()))
                        {
                            isGoodSymbol = false;
                            reason += "(" + p.BasicType() + " is not known to angelscript)";
                        }
                        first = false;
                    }
                    paramListForAngelscriptSignature = paramListForAngelscriptSignature.Replace("std::string", "string");
                    funcPtrType += paramList + ")";
                    if (f.isConst)
                        funcPtrType += " const";

                    if (f.parameters.Count > 16 && isGoodSymbol)
                    {
                        isGoodSymbol = false;
                        reason += "(Generic binding doesn't support more than 4 parameters)";
                    }

                    if (!isGoodSymbol)
                        t += "// /*" + reason + "*/ ";

                    t += "\t";

                    if (isCtor)
                    {
                        string paramListAsIdentifier = paramList.Replace(",", "_").Replace(" ", "_").Replace("&", "ref").Replace("*", "ptr");
                        t += "r = engine->RegisterObjectBehaviour(\"" + className + "\", asBEHAVE_CONSTRUCT, \"void f(" + paramListForAngelscriptSignature + ")\", AS_CONSTRUCTOR(" + className + "_ctor_" + paramListAsIdentifier + ", " + className + ", (" + paramList + ")), AS_CTOR_CONVENTION); assert(r >= 0);\n";
                    }
                    else
                    {
                        if (f.isStatic)
                            t += "//.classmethod(";
                        else
                        {
                            string funcNameForAngelscript = f.name;
                            funcNameForAngelscript = funcNameForAngelscript.Replace("operator+", "opAdd").Replace("operator-", "opSub").Replace("operator*", "opMul").Replace("operator/", "opDiv")
                                .Replace("operator+=", "opAddAssign").Replace("operator-=", "opSubAssign").Replace("operator*=", "opMulAssign").Replace("operator/=", "opDivAssign")
                                .Replace("operator=", "opEquals");
                            t += "r = engine->RegisterObjectMethod(\"" + className + "\", \"" + f.type.Replace("std::string", "string") + " " + funcNameForAngelscript + "(" + paramListForAngelscriptSignature + ")"
                                + (f.isConst ? " const" : "") + "\", AS_METHOD_FUNCTION_PR(" + className + ", " + f.name + ", (" + paramList
                                + ")" + (f.isConst ? " const" : "") + ", " + f.type + "), AS_MEMBER_CALL_CONVENTION); assert(r >= 0);\n";
                        }
                    }
                }
                else if (f.kind == "variable" && f.visibilityLevel == VisibilityLevel.Public)
                {
                    if (!knownSymbolNames.Contains(f.type))
                        t += "// /* " + f.type + " is not known to angelscript. */ ";
                    else if (f.IsArray())
                        t += "// /* Exposing array types as fields are not supported by angelscript. */ ";
                    else if (f.isStatic)
                        t += "// /* Exposing static class variables not yet implemented (are they supported?) */ ";
                    t += "\t";
                    t += "r = engine->RegisterObjectProperty(\"" + f.parent.name + "\", \"" + f.type + " " + f.name + "\", asOFFSET(" + f.parent.name + ", " + f.name + ")); assert(r >= 0);\n";
                }
            }
            t += "\t;\n";

            tw.Write(t);
        }
    }
}
