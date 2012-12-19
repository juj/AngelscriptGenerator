using System.Xml;
using System.IO;
using System.Web;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocGenerator;

namespace AngelscriptGenerator
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
                "#include <angelscript.h>\n\n" +

                "// If you want to use Angelscript's generic calling convention, #define USE_ANGELSCRIPT_GENERIC_CALL_CONVENTION before including this file.\n" +
                "// If you use the generic calling convention, you MUST #include <autowrapper/aswrappedcall.h> to bring the wrapper code. If your functions contain a large amount of input parameters,\n" +
                "// copy the file cpp/aswrappedcall_17.h to your project and include that file instead.\n" +
                "#if defined(USE_ANGELSCRIPT_GENERIC_CALL_CONVENTION)\n\n" +
                "#define AS_CALL_CONVENTION asCALL_GENERIC\n" +
                "#define AS_CTOR_CONVENTION asCALL_GENERIC\n" +
                "#define AS_MEMBER_CALL_CONVENTION asCALL_GENERIC\n" +
                "#define AS_FUNCTION WRAP_FN\n" +
                "#define AS_CONSTRUCTOR(ctorFuncName, className, parameters) WRAP_CON(className, parameters)\n" +
                "#define AS_DESTRUCTOR(className, dtorFunc) WRAP_DES(className)\n" +
                "#define AS_METHOD_FUNCTION_PR WRAP_MFN_PR\n\n" +
                "#else\n\n" +
                "#define AS_CALL_CONVENTION asCALL_CDECL\n" +
                "#define AS_CTOR_CONVENTION asCALL_CDECL_OBJLAST\n" +
                "#define AS_MEMBER_CALL_CONVENTION asCALL_THISCALL\n" +
                "#define AS_FUNCTION asFUNCTION\n" +
                "#define AS_CONSTRUCTOR(ctorFuncName, className, parameters) asFUNCTION(ctorFuncName)\n" +
                "#define AS_DESTRUCTOR(className, dtorFunc) asFUNCTION(dtorFunc)\n" +
                "#define AS_METHOD_FUNCTION_PR asMETHODPR\n\n" +
                "#endif\n\n";

            tw.Write(t);

            for (int i = 1; i < args.Length; ++i)
                GenerateCtorFunctions(args[i]);

            t = "void RegisterAngelscriptObjects(asIScriptEngine *engine)\n" +
                 "{\n" +
                 "\tint r;\n\n";
            tw.Write(t);

            for (int i = 1; i < args.Length; ++i)
                RegisterObjectType(args[i]);

            tw.Write("\n\n");

            for (int i = 1; i < args.Length; ++i)
                GenerateBindingsFile(args[i], knownSymbolNames);

            tw.Write("}\n");

            tw.Flush();
            tw.Close();

            Console.WriteLine("Writing angelscript_symbols_cpp.h done.");
        }

        static TextWriter tw = new StreamWriter("angelscript_symbols_cpp.h");

        static string AngelscriptFlags(List<string> attributes)
        {
            foreach (string s in attributes)
            {
                Match m = Regex.Match(s, "ascript\\s*:\\s*(.*)");
                if (m.Success)
                    return m.Groups[1].Value.Trim();
            }
            return "";
        }

        static bool IsReferenceType(Symbol clazz)
        {
            string flags = AngelscriptFlags(clazz.attributes);
            if (flags.Contains("asOBJ_REF"))
                return true;
            if (flags.Contains("asOBJ_VALUE"))
                return false;

            // If class has pure virtual functions, it must be a reference type.
            if (clazz.IsClassAbstract())
                return true;

            List<Symbol> ctors = clazz.AllClassCtors();
            bool hasPublicCtors = false;
            foreach (Symbol s in ctors)
                if (s.visibilityLevel == VisibilityLevel.Public)
                {
                    hasPublicCtors = true;
                    break;
                }

            // If all class ctors are non-public, it must be a reference type.
            if (ctors.Count > 0 && !hasPublicCtors)
                return true;

            // If class copy-ctor is non-public, it must be a reference type. (well, strictly, not, but it's so silly if not, so take it as a rule)
            Symbol copyCtor = clazz.ClassCopyCtor();
            if (copyCtor != null && copyCtor.visibilityLevel != VisibilityLevel.Public)
                return true;

            // If class assignment operator is non-public, it must be a reference type.
            Symbol assignmentOp = clazz.FindChildByName("operator=");
            if (assignmentOp != null && assignmentOp.visibilityLevel != VisibilityLevel.Public)
                return true;

            Symbol dtor = clazz.ClassDtor();
            if (dtor != null && dtor.visibilityLevel != VisibilityLevel.Public) // Non-public ctor? -> reference type
                return true;

            return false;
        }

        static void RegisterObjectType(string className)
        {
            if (!cs.symbolsByName.ContainsKey(className))
            {
                Console.WriteLine("Error: Cannot generate bindings for class '" + className + "', XML docs for that class don't exist!");
                return;
            }

            Symbol clazz = cs.symbolsByName[className];

            bool hasCtor = clazz.ClassCtor() != null;
            bool hasDtor = clazz.ClassDtor() != null;
            bool hasCopyCtor = clazz.ClassCopyCtor() != null;
            bool hasAssignmentOp = clazz.ClassAssignmentOperator() != null;
            bool isReferenceType = IsReferenceType(clazz);

            // Register a value type
            // TODO: Add support to user to choose between these:
            string flags = AngelscriptFlags(clazz.attributes);
            if (flags.Length == 0)
            {
                if (isReferenceType)
                    flags = "asOBJ_REF | asOBJ_NOCOUNT"; ///\todo When to set asOBJ_NOCOUNT?
                else
                    flags = "asOBJ_VALUE | asOBJ_POD"; ///\todo When to set asOBJ_NOCOUNT?
            }

            if (!isReferenceType)
            {
                if (hasCtor) flags += " | asOBJ_APP_CLASS_CONSTRUCTOR";
                if (hasDtor) flags += " | asOBJ_APP_CLASS_DESTRUCTOR";
                if (hasCopyCtor) flags += " | asOBJ_APP_CLASS_COPY_CONSTRUCTOR";
                if (hasAssignmentOp) flags += " | asOBJ_APP_CLASS_ASSIGNMENT";
            }

            string t = "\tr = engine->RegisterObjectType(\"" + className + "\", sizeof(" + className + "), " + flags + "); assert(r >= 0);\n";
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
                if (f.visibilityLevel != VisibilityLevel.Public)
                    continue; // Only public ctors and dtors are exposed.

                bool isCtor = f.name == className;
                bool isDtor = (f.name == "~" + className);
                if (!isCtor && !isDtor)
                    continue;

                if (isCtor)
                {
                    if (!s.IsClassAbstract())
                    {
                        string paramList = f.FunctionParameterListWithoutNames();

                        string paramListAsIdentifier = paramList.Replace(",", "_").Replace(" ", "_").Replace("&", "ref").Replace("*", "ptr");
                        string args = f.ArgStringWithTypes();
                        args = args.Substring(1, args.Length - 2);
                        string args2 = f.ArgStringWithoutTypes();
                        args2 = args2.Substring(1, args2.Length - 2);
                        if (args.Length > 0)
                            args += ", ";

                        if (IsReferenceType(s)) // This is a ref type - implement factory functions.
                        {
                            t += "static " + className + " *" + className + "_Factory_" + paramListAsIdentifier + "(" + args + className + " *self)\n";
                            t += "{\n";
                            t += "\treturn new " + className + f.ArgStringWithoutTypes() + ";\n";
                            t += "}\n\n";
                        }
                        else // This is a value type - implement placement new ctor functions.
                        {
                            t += "static void " + className + "_ctor_" + paramListAsIdentifier + "(" + args + className + " *self)\n";
                            t += "{\n";
                            t += "\tnew(self) " + className + f.ArgStringWithoutTypes() + ";\n";
                            t += "}\n\n";
                        }
                    }
                }
                else // Dtor
                {
                    t += "static void " + className + "_dtor(void *memory)\n";
                    t += "{\n";
                    t += "\t((" + className + "*)memory)->~" + className + "();\n";
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
                    bool isDtor = (f.name == "~" + className);

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

                    if (f.name == "operator!=")
                    {
                        isGoodSymbol = false;
                        reason += "(operator != is implemented automatically by exposing operator == as opEquals)";
                    }
                    if (f.name == "operator<" || f.name == "operator<=" || f.name == "operator>" || f.name == "operator>=")
                    {
                        isGoodSymbol = false;
                        reason += "(operators <, <=, > and >= are implemented by exposing operator opCmp)";
                    }

                    if (f.name.StartsWith("operator "))
                    {
                        isGoodSymbol = false;
                        reason += "(Implicit conversion operators are not supported)";
                    }
                    if (!isGoodSymbol)
                        t += "// /*" + reason + "*/ ";

                    t += "\t";

                    if (isCtor)
                    {
                        if (!s.IsClassAbstract())
                        {
                            string paramListAsIdentifier = paramList.Replace(",", "_").Replace(" ", "_").Replace("&", "ref").Replace("*", "ptr");

                            if (IsReferenceType(s)) // Reference types have factories.
                                t += "r = engine->RegisterObjectBehaviour(\"" + className + "\", asBEHAVE_FACTORY, \"" + className + "@ f(" + paramListForAngelscriptSignature + ")\", AS_FUNCTION(" + className + "_ctor_" + paramListAsIdentifier + ", " + className + ", (" + paramList + ")), AS_CALL_CONVENTION); assert(r >= 0);";
                            else // Value types have constructors.
                                t += "r = engine->RegisterObjectBehaviour(\"" + className + "\", asBEHAVE_CONSTRUCT, \"void f(" + paramListForAngelscriptSignature + ")\", AS_CONSTRUCTOR(" + className + "_ctor_" + paramListAsIdentifier + ", " + className + ", (" + paramList + ")), AS_CTOR_CONVENTION); assert(r >= 0);\n";
                        }
                    }
                    else if (isDtor)
                    {
                        t += "r = engine->RegisterObjectBehaviour(\"" + className + "\", asBEHAVE_DESTRUCT, \"void f()\", AS_DESTRUCTOR(" + className + ", " + className + "_dtor), AS_CTOR_CONVENTION); assert(r >= 0);";
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
                                .Replace("operator==", "opEquals");
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
            t += "\n\n\n";

            tw.Write(t);
        }
    }
}
