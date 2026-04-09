#include "SampleCppCli.h"
#include <cstdlib>

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;

namespace SampleCppCli {

    // ========== ResourceBase ==========

    ResourceBase::ResourceBase(String^ name, int bufferSize)
        : _disposed(false), _name(name), _operationCount(0),
          _bufferSize(bufferSize)
    {
        _nativeBuffer = new int[bufferSize];
        for (int i = 0; i < bufferSize; i++)
            _nativeBuffer[i] = i;
    }

    ResourceBase::~ResourceBase()
    {
        if (!_disposed)
        {
            _disposed = true;
            OnDisposing();

            if (_nativeBuffer != nullptr)
            {
                delete[] _nativeBuffer;
                _nativeBuffer = nullptr;
            }

            GC::SuppressFinalize(this);
        }
    }

    ResourceBase::!ResourceBase()
    {
        if (_nativeBuffer != nullptr)
        {
            delete[] _nativeBuffer;
            _nativeBuffer = nullptr;
        }
    }

    int ResourceBase::Execute(int input)
    {
        ThrowIfDisposed();
        _operationCount++;
        return input;
    }

    String^ ResourceBase::GetStatus()
    {
        return String::Format("ResourceBase[{0}]: ops={1}, disposed={2}",
            _name, _operationCount, _disposed);
    }

    void ResourceBase::OnDisposing()
    {
        // Base does nothing; derived classes can override
    }

    String^ ResourceBase::DisplayName::get()
    {
        return _name;
    }

    int ResourceBase::OperationCount::get()
    {
        return _operationCount;
    }

    bool ResourceBase::IsDisposed::get()
    {
        return _disposed;
    }

    void ResourceBase::ThrowIfDisposed()
    {
        if (_disposed)
            throw gcnew ObjectDisposedException(_name);
    }

    // ========== DoubleProcessor ==========

    DoubleProcessor::DoubleProcessor(String^ name, int bufferSize, int factor)
        : ResourceBase(name, bufferSize), _factor(factor)
    {
    }

    int DoubleProcessor::Execute(int input)
    {
        ResourceBase::Execute(input);
        return input * _factor;
    }

    String^ DoubleProcessor::GetStatus()
    {
        return String::Format("DoubleProcessor[{0}]: factor={1}, ops={2}",
            DisplayName, _factor, OperationCount);
    }

    void DoubleProcessor::OnDisposing()
    {
        _factor = 0;
    }

    int DoubleProcessor::Process(int value)
    {
        return Execute(value);
    }

    void DoubleProcessor::Reset()
    {
        _factor = 1;
    }

    // ========== AccumulatingProcessor ==========

    AccumulatingProcessor::AccumulatingProcessor(String^ name, int bufferSize, int factor)
        : DoubleProcessor(name, bufferSize, factor), _accumulated(0)
    {
    }

    int AccumulatingProcessor::Execute(int input)
    {
        int result = DoubleProcessor::Execute(input);
        _accumulated += result;
        return result;
    }

    String^ AccumulatingProcessor::GetStatus()
    {
        return String::Format("AccumulatingProcessor[{0}]: accumulated={1}, ops={2}",
            DisplayName, _accumulated, OperationCount);
    }

    int AccumulatingProcessor::Accumulated::get()
    {
        return _accumulated;
    }

    // ========== SimpleResource ==========

    SimpleResource::SimpleResource(int value)
        : _disposed(false), _value(value)
    {
    }

    SimpleResource::~SimpleResource()
    {
        _disposed = true;
        GC::SuppressFinalize(this);
    }

    SimpleResource::!SimpleResource()
    {
    }

    int SimpleResource::GetValue()
    {
        if (_disposed)
            throw gcnew ObjectDisposedException("SimpleResource");
        return _value;
    }

    bool SimpleResource::IsDisposed::get()
    {
        return _disposed;
    }

    // ========== NativeProcessor (pure native class) ==========

#pragma unmanaged

    NativeProcessor::NativeProcessor(int multiplier) : _multiplier(multiplier) {}
    NativeProcessor::~NativeProcessor() { _multiplier = 0; }

    int NativeProcessor::ProcessNative(int value)
    {
        return value * _multiplier;
    }

    int NativeProcessor::GetMultiplier() const
    {
        return _multiplier;
    }

#pragma managed

    // ========== NativeBridge ==========

    NativeBridge::NativeBridge(int multiplier) : _disposed(false)
    {
        LastResult = 0;
        _native = new NativeProcessor(multiplier);
    }

    NativeBridge::~NativeBridge()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_native != nullptr)
            {
                delete _native;
                _native = nullptr;
            }
            GC::SuppressFinalize(this);
        }
    }

    NativeBridge::!NativeBridge()
    {
        if (_native != nullptr)
        {
            delete _native;
            _native = nullptr;
        }
    }

    int NativeBridge::Transform(int value)
    {
        return value + 1;
    }

    void NativeBridge::OnCompleted(int result)
    {
        LastResult = result;
    }

    int NativeBridge::ExecuteNative(int input)
    {
        if (_disposed || _native == nullptr)
            throw gcnew ObjectDisposedException("NativeBridge");

        // Call native code
        int nativeResult = _native->ProcessNative(input);

        // Call virtual managed methods (these generate native->managed thunks)
        int transformed = Transform(nativeResult);
        OnCompleted(transformed);

        return transformed;
    }

    bool NativeBridge::IsDisposed::get()
    {
        return _disposed;
    }

    // ========== CallbackReceiver ==========

    CallbackReceiver::CallbackReceiver()
        : _dataSum(0), _lastError(nullptr), _transformCount(0)
    {
    }

    void CallbackReceiver::OnData(int data)
    {
        _dataSum += data;
    }

    void CallbackReceiver::OnError(String^ message)
    {
        _lastError = message;
    }

    int CallbackReceiver::OnTransform(int input)
    {
        _transformCount++;
        return input * 2;
    }

    int CallbackReceiver::DataSum::get() { return _dataSum; }
    String^ CallbackReceiver::LastError::get() { return _lastError; }
    int CallbackReceiver::TransformCount::get() { return _transformCount; }

    // ========== Helper: native function that invokes managed callback via interface ==========

    // This function is called from managed code but calls back through interface
    // The interface dispatch through INativeCallback generates VTableFixup entries
    static void InvokeCallbacksFromNativeContext(INativeCallback^ callback, array<int>^ data)
    {
        for (int i = 0; i < data->Length; i++)
        {
            int transformed = callback->OnTransform(data[i]);
            callback->OnData(transformed);
        }
    }

    // ========== Verifier ==========

    String^ Verifier::TestVirtualDispatch()
    {
        auto sb = gcnew StringBuilder();

        ResourceBase^ base1 = gcnew DoubleProcessor("dp1", 4, 3);
        int result = base1->Execute(10);
        sb->AppendFormat("VirtualDispatch: Execute(10)={0}", result);

        if (result != 30)
        {
            sb->Append(" FAIL(expected 30)");
            delete base1;
            return sb->ToString();
        }

        String^ status = base1->GetStatus();
        sb->AppendFormat(", Status={0}", status);

        if (!status->Contains("DoubleProcessor"))
        {
            sb->Append(" FAIL(wrong type in status)");
            delete base1;
            return sb->ToString();
        }

        delete base1;
        sb->Append(" OK");
        return sb->ToString();
    }

    String^ Verifier::TestDispose()
    {
        auto sb = gcnew StringBuilder();

        // Test 1: ResourceBase derived Dispose
        {
            auto proc = gcnew DoubleProcessor("dp_dispose", 4, 2);
            bool disposedBefore = proc->IsDisposed;
            delete proc;
            bool disposedAfter = proc->IsDisposed;

            sb->AppendFormat("Dispose: before={0}, after={1}", disposedBefore, disposedAfter);

            if (disposedBefore || !disposedAfter)
            {
                sb->Append(" FAIL");
                return sb->ToString();
            }
        }

        // Test 2: IDisposable cast and delete
        {
            ResourceBase^ base2 = gcnew DoubleProcessor("dp_idisposable", 4, 2);
            IDisposable^ disposable = safe_cast<IDisposable^>(base2);
            delete disposable;

            bool disposedAfter = base2->IsDisposed;
            sb->AppendFormat(", IDisposable.Dispose: disposed={0}", disposedAfter);

            if (!disposedAfter)
            {
                sb->Append(" FAIL");
                return sb->ToString();
            }
        }

        // Test 3: SimpleResource Dispose
        {
            auto simple = gcnew SimpleResource(42);
            int val = simple->GetValue();
            delete simple;
            bool disposed = simple->IsDisposed;

            sb->AppendFormat(", SimpleResource: val={0}, disposed={1}", val, disposed);

            if (val != 42 || !disposed)
            {
                sb->Append(" FAIL");
                return sb->ToString();
            }
        }

        sb->Append(" OK");
        return sb->ToString();
    }

    String^ Verifier::TestInterfaceDispatch()
    {
        auto sb = gcnew StringBuilder();

        auto proc = gcnew DoubleProcessor("dp_iface", 4, 5);

        IProcessor^ iproc = safe_cast<IProcessor^>(proc);
        int result = iproc->Process(7);
        sb->AppendFormat("InterfaceDispatch: Process(7)={0}", result);

        if (result != 35)
        {
            sb->Append(" FAIL(expected 35)");
            delete proc;
            return sb->ToString();
        }

        iproc->Reset();

        INameable^ nameable = safe_cast<INameable^>(proc);
        String^ name = nameable->DisplayName;
        sb->AppendFormat(", Name={0}", name);

        if (name != "dp_iface")
        {
            sb->Append(" FAIL(wrong name)");
            delete proc;
            return sb->ToString();
        }

        delete proc;
        sb->Append(" OK");
        return sb->ToString();
    }

    String^ Verifier::TestInheritanceChain()
    {
        auto sb = gcnew StringBuilder();

        auto accum = gcnew AccumulatingProcessor("accum", 4, 2);

        ResourceBase^ baseRef = accum;
        baseRef->Execute(5);  // 5*2 = 10, accumulated = 10
        baseRef->Execute(3);  // 3*2 = 6, accumulated = 16

        int accumulated = accum->Accumulated;
        int opCount = baseRef->OperationCount;
        String^ status = baseRef->GetStatus();

        sb->AppendFormat("InheritanceChain: accumulated={0}, ops={1}", accumulated, opCount);

        if (accumulated != 16 || opCount != 2)
        {
            sb->AppendFormat(" FAIL(expected accumulated=16, ops=2)");
            delete accum;
            return sb->ToString();
        }

        if (!status->Contains("AccumulatingProcessor"))
        {
            sb->AppendFormat(" FAIL(wrong status: {0})", status);
            delete accum;
            return sb->ToString();
        }

        delete accum;
        bool disposed = baseRef->IsDisposed;
        sb->AppendFormat(", disposed={0}", disposed);

        if (!disposed)
        {
            sb->Append(" FAIL(not disposed)");
            return sb->ToString();
        }

        sb->Append(" OK");
        return sb->ToString();
    }

    String^ Verifier::TestNativeBridge()
    {
        auto sb = gcnew StringBuilder();

        auto bridge = gcnew NativeBridge(3);

        // ExecuteNative: native(5*3=15) -> Transform(15+1=16) -> OnCompleted(16)
        int result = bridge->ExecuteNative(5);
        sb->AppendFormat("NativeBridge: ExecuteNative(5)={0}, LastResult={1}",
            result, bridge->LastResult);

        if (result != 16 || bridge->LastResult != 16)
        {
            sb->Append(" FAIL(expected 16)");
            delete bridge;
            return sb->ToString();
        }

        // Test Dispose
        delete bridge;
        bool disposed = bridge->IsDisposed;
        sb->AppendFormat(", disposed={0}", disposed);

        if (!disposed)
        {
            sb->Append(" FAIL(not disposed)");
            return sb->ToString();
        }

        sb->Append(" OK");
        return sb->ToString();
    }

    String^ Verifier::TestNativeCallback()
    {
        auto sb = gcnew StringBuilder();

        auto receiver = gcnew CallbackReceiver();
        auto data = gcnew array<int>{ 1, 2, 3, 4, 5 };

        // Invoke callbacks through interface dispatch
        // Each element: OnTransform(x) -> x*2, then OnData(x*2)
        // DataSum = 2+4+6+8+10 = 30, TransformCount = 5
        InvokeCallbacksFromNativeContext(receiver, data);

        sb->AppendFormat("NativeCallback: DataSum={0}, TransformCount={1}",
            receiver->DataSum, receiver->TransformCount);

        if (receiver->DataSum != 30)
        {
            sb->AppendFormat(" FAIL(expected DataSum=30)");
            return sb->ToString();
        }

        if (receiver->TransformCount != 5)
        {
            sb->AppendFormat(" FAIL(expected TransformCount=5)");
            return sb->ToString();
        }

        sb->Append(" OK");
        return sb->ToString();
    }

    String^ Verifier::RunAllTests()
    {
        auto sb = gcnew StringBuilder();
        sb->AppendLine(TestVirtualDispatch());
        sb->AppendLine(TestDispose());
        sb->AppendLine(TestInterfaceDispatch());
        sb->AppendLine(TestInheritanceChain());
        sb->AppendLine(TestNativeBridge());
        sb->AppendLine(TestNativeCallback());

        // Count "OK" occurrences
        String^ result = sb->ToString();
        int okCount = 0;
        int idx = 0;
        while ((idx = result->IndexOf(" OK", idx)) >= 0)
        {
            okCount++;
            idx += 3;
        }

        sb->AppendFormat("CHECKSUM:{0}/6", okCount);
        return sb->ToString();
    }
}
