using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Windows.Documents;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Torch.Managers.PatchManager.Transpile;
using Torch.Utils;
using VRage.Game.VisualScripting;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;
using OperandType = System.Reflection.Emit.OperandType;

namespace Torch.Managers.PatchManager.MSIL
{
    /// <summary>
    ///     Represents a single MSIL instruction, and its operand
    /// </summary>
    public class MsilInstruction
    {
        /// <summary>
        ///     Creates a new instruction with the given opcode.
        /// </summary>
        /// <param name="opcode">Opcode</param>
        public MsilInstruction(OpCode opcode)
        {
            OpCode = opcode;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    Operand = null;
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.InlineBrTarget:
                    Operand = new MsilOperandBrTarget(this);
                    break;
                case OperandType.InlineField:
                    Operand = new MsilOperandInline.MsilOperandReflected<FieldInfo>(this);
                    break;
                case OperandType.ShortInlineI:
                case OperandType.InlineI:
                    Operand = new MsilOperandInline.MsilOperandInt32(this);
                    break;
                case OperandType.InlineI8:
                    Operand = new MsilOperandInline.MsilOperandInt64(this);
                    break;
                case OperandType.InlineMethod:
                    Operand = new MsilOperandInline.MsilOperandReflected<MethodBase>(this);
                    break;
                case OperandType.InlineR:
                    Operand = new MsilOperandInline.MsilOperandDouble(this);
                    break;
                case OperandType.InlineSig:
                    Operand = new MsilOperandInline.MsilOperandSignature(this);
                    break;
                case OperandType.InlineString:
                    Operand = new MsilOperandInline.MsilOperandString(this);
                    break;
                case OperandType.InlineSwitch:
                    Operand = new MsilOperandSwitch(this);
                    break;
                case OperandType.InlineTok:
                    Operand = new MsilOperandInline.MsilOperandReflected<MemberInfo>(this);
                    break;
                case OperandType.InlineType:
                    Operand = new MsilOperandInline.MsilOperandReflected<Type>(this);
                    break;
                case OperandType.ShortInlineVar:
                case OperandType.InlineVar:
                    if (OpCode.IsLocalStore() || OpCode.IsLocalLoad() || OpCode.IsLocalLoadByRef())
                        Operand = new MsilOperandInline.MsilOperandLocal(this);
                    else
                        Operand = new MsilOperandInline.MsilOperandArgument(this);
                    break;
                case OperandType.ShortInlineR:
                    Operand = new MsilOperandInline.MsilOperandSingle(this);
                    break;
#pragma warning disable 618
                case OperandType.InlinePhi:
#pragma warning restore 618
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public MsilInstruction(CodeInstruction instruction) : this(instruction.opcode)
        {
            switch (instruction.operand)
            {
                case LocalBuilder builder when Operand is MsilOperandInline.MsilOperandLocal operandLocal:
                    operandLocal.Value = new(builder);
                    break;
                case MethodBase methodBase when Operand is MsilOperandInline.MsilOperandReflected<MethodBase> operandMethod:
                    operandMethod.Value = methodBase;
                    break;
                case Type type when Operand is MsilOperandInline.MsilOperandReflected<Type> operandType:
                    operandType.Value = type;
                    break;
                case MemberInfo info when Operand is MsilOperandInline.MsilOperandReflected<MemberInfo> operandMember:
                    operandMember.Value = info;
                    break;
                case IEnumerable<Label> labels when Operand is MsilOperandSwitch operandSwitch:
                    operandSwitch.Labels = labels.Select(b => new MsilLabel(b)).ToArray();
                    break;
                case float single when Operand is MsilOperandInline.MsilOperandSingle operandSingle:
                    operandSingle.Value = single;
                    break;
                case FieldInfo fieldInfo when Operand is MsilOperandInline.MsilOperandReflected<FieldInfo> operandField:
                    operandField.Value = fieldInfo;
                    break;
                case string str when Operand is MsilOperandInline.MsilOperandString operandString:
                    operandString.Value = str;
                    break;
                case SignatureHelper signatureHelper when Operand is MsilOperandInline.MsilOperandSignature operandSignature:
                    operandSignature.Value = signatureHelper;
                    break;
                case int int32 when Operand is MsilOperandInline.MsilOperandInt32 operandInt32:
                    operandInt32.Value = int32;
                    break;
                case long int64 when Operand is MsilOperandInline.MsilOperandInt64 operandInt64:
                    operandInt64.Value = int64;
                    break;
                case Label label when Operand is MsilOperandBrTarget operandBrTarget:
                    operandBrTarget.Target = new(label);
                    break;
                case double @double when Operand is MsilOperandInline.MsilOperandDouble operandDouble:
                    operandDouble.Value = @double;
                    break;
                case byte @byte when Operand is MsilOperandInline.MsilOperandArgument operandArgument:
                    operandArgument.Value = new(@byte);
                    break;
                case byte @byte when Operand is MsilOperandInline.MsilOperandLocal operandArgument:
                    operandArgument.Value = new(@byte);
                    break;
            }

            Labels = instruction.labels.Select(b => new MsilLabel(b)).ToHashSet();
            TryCatchOperations =
                instruction.blocks.Select(
                    b => new MsilTryCatchOperation((MsilTryCatchOperationType)b.blockType, b.catchType == typeof(object) ? null : b.catchType)).ToList();
        }

        internal CodeInstruction ToCodeIns(LoggingIlGenerator generator)
        {
            var ins = new CodeInstruction(OpCode, Operand switch
            {
                MsilOperandBrTarget msilOperandBrTarget => msilOperandBrTarget.Target.LabelFor(generator),
                MsilOperandInline.MsilOperandReflected<MethodBase> msilOperandReflected => msilOperandReflected.Value,
                MsilOperandInline.MsilOperandReflected<MemberInfo> msilOperandReflected => msilOperandReflected.Value,
                MsilOperandInline.MsilOperandReflected<FieldInfo> msilOperandReflected => msilOperandReflected.Value,
                MsilOperandInline.MsilOperandReflected<Type> msilOperandReflected => msilOperandReflected.Value,
                MsilOperandInline.MsilOperandDouble msilOperandDouble => msilOperandDouble.Value,
                MsilOperandInline.MsilOperandInt32 msilOperandInt32 => msilOperandInt32.Value,
                MsilOperandInline.MsilOperandInt64 msilOperandInt64 => msilOperandInt64.Value,
                MsilOperandInline.MsilOperandLocal msilOperandLocal when OpCode.OperandType == OperandType.InlineVar => msilOperandLocal.Value.Local,
                MsilOperandInline.MsilOperandLocal msilOperandLocal when OpCode.OperandType == OperandType.ShortInlineVar => (byte)msilOperandLocal.Value.Index,
                MsilOperandInline.MsilOperandArgument msilOperandArgument when OpCode.OperandType == OperandType.InlineVar => msilOperandArgument.Value,
                MsilOperandInline.MsilOperandArgument msilOperandArgument when OpCode.OperandType == OperandType.ShortInlineVar => (byte)msilOperandArgument.Value.Position,
                MsilOperandInline.MsilOperandSignature msilOperandSignature => msilOperandSignature.Value,
                MsilOperandInline.MsilOperandSingle msilOperandSingle => msilOperandSingle.Value,
                MsilOperandInline.MsilOperandString msilOperandString => msilOperandString.Value,
                MsilOperandSwitch msilOperandSwitch => msilOperandSwitch.Labels.Select(b => b.LabelFor(generator)).ToArray(),
                _ => null
            })
            {
                labels = Labels.Select(b => b.LabelFor(generator)).ToList(),
                blocks = TryCatchOperations.Select(b => new ExceptionBlock((ExceptionBlockType)b.Type, b.CatchType))
                                           .ToList()
            };

            return ins;
        }

        /// <summary>
        ///     Opcode of this instruction
        /// </summary>
        public OpCode OpCode { get; }

        /// <summary>
        ///     Raw memory offset of this instruction; optional.
        /// </summary>
        public int Offset { get; internal set; }

        /// <summary>
        ///     The operand for this instruction, or null.
        /// </summary>
        public MsilOperand Operand { get; internal set; }

        /// <summary>
        ///     Labels pointing to this instruction.
        /// </summary>
        public HashSet<MsilLabel> Labels { get; } = new HashSet<MsilLabel>();

        /// <summary>
        /// The try catch operation that is performed here.
        /// </summary>
        [Obsolete("Since instructions can have multiple try catch operations you need to be using TryCatchOperations")]
        public MsilTryCatchOperation TryCatchOperation
        {
            get => TryCatchOperations.FirstOrDefault();
            set
            {
                TryCatchOperations.Clear();
                if (value != null)
                    TryCatchOperations.Add(value);
            }
        }

        /// <summary>
        /// The try catch operations performed here, in order from first to last.
        /// </summary>
        public readonly List<MsilTryCatchOperation> TryCatchOperations = new List<MsilTryCatchOperation>();

        private static readonly ConcurrentDictionary<Type, PropertyInfo> _setterInfoForInlines = new ConcurrentDictionary<Type, PropertyInfo>();

        /// <summary>
        ///     Sets the inline value for this instruction.
        /// </summary>
        /// <typeparam name="T">The type of the inline constraint</typeparam>
        /// <param name="o">Value</param>
        /// <returns>This instruction</returns>
        public MsilInstruction InlineValue<T>(T o)
        {
            Type type = typeof(T);
            while (type != null)
            {
                if (!_setterInfoForInlines.TryGetValue(type, out PropertyInfo target))
                {
                    Type genType = typeof(MsilOperandInline<>).MakeGenericType(type);
                    target = genType.GetProperty(nameof(MsilOperandInline<int>.Value));
                    _setterInfoForInlines[type] = target;
                }

                Debug.Assert(target?.DeclaringType != null);
                if (target.DeclaringType.IsInstanceOfType(Operand))
                {
                    target.SetValue(Operand, o);
                    return this;
                }

                type = type.BaseType;
            }

            if (Operand is not MsilOperandInline<T> operandInline)
                throw new InvalidOperationException(
                    $"Type {typeof(T).FullName} is not valid operand for {Operand?.GetType().Signature()}");

            operandInline.Value = o;
            return this;
        }

        /// <summary>
        /// Makes a copy of the instruction with a new opcode.
        /// </summary>
        /// <param name="newOpcode">The new opcode</param>
        /// <returns>The copy</returns>
        public MsilInstruction CopyWith(OpCode newOpcode)
        {
            var result = new MsilInstruction(newOpcode);
            Operand?.CopyTo(result.Operand);
            foreach (MsilLabel x in Labels)
                result.Labels.Add(x);
            foreach (var op in TryCatchOperations)
                result.TryCatchOperations.Add(op);
            return result;
        }

        /// <summary>
        /// Adds the given label to this instruction
        /// </summary>
        /// <param name="label">Label to add</param>
        /// <returns>this instruction</returns>
        public MsilInstruction LabelWith(MsilLabel label)
        {
            Labels.Add(label);
            return this;
        }

        /// <summary>
        ///     Sets the inline branch target for this instruction.
        /// </summary>
        /// <param name="label">Target to jump to</param>
        /// <returns>This instruction</returns>
        public MsilInstruction InlineTarget(MsilLabel label)
        {
            ((MsilOperandBrTarget) Operand).Target = label;
            return this;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (MsilLabel label in Labels)
                sb.Append(label).Append(": ");
            sb.Append(OpCode.Name).Append("\t").Append(Operand);
            return sb.ToString();
        }


#pragma warning disable 649
        [ReflectedMethod(Name = "StackChange")]
        private static Func<OpCode, int> _stackChange;
#pragma warning restore 649

        /// <summary>
        /// Estimates the stack delta for this instruction.
        /// </summary>
        /// <returns>Stack delta</returns>
        public int StackChange()
        {
            int num = _stackChange.Invoke(OpCode);
            if ((OpCode == OpCodes.Call || OpCode == OpCodes.Callvirt || OpCode == OpCodes.Newobj) &&
                Operand is MsilOperandInline<MethodBase> inline)
            {
                MethodBase op = inline.Value;
                if (op == null)
                    return num;
                if (op is MethodInfo mi && mi.ReturnType != typeof(void))
                    num++;
                num -= op.GetParameters().Length;
                if (!op.IsStatic && OpCode != OpCodes.Newobj)
                    num--;
            }

            return num;
        }

        /// <summary>
        /// Gets the maximum amount of space this instruction will use.
        /// </summary>
        public int MaxBytes => 2 + (Operand?.MaxBytes ?? 0);
    }
}
