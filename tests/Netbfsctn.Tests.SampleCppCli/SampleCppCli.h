#pragma once

using namespace System;

namespace SampleCppCli {

    // Custom interface defined in C++/CLI (module-internal)
    public interface class IProcessor {
        int Process(int value);
        void Reset();
    };

    // Another interface for multiple interface implementation
    public interface class INameable {
        property String^ DisplayName { String^ get(); };
    };

    // Abstract base class with virtual methods
    public ref class ResourceBase abstract : public IDisposable, public INameable {
    private:
        bool _disposed;
        String^ _name;
        int _operationCount;

    protected:
        // Native resource simulation
        int* _nativeBuffer;
        int _bufferSize;

    public:
        ResourceBase(String^ name, int bufferSize);

        // C++/CLI destructor -> IDisposable::Dispose()
        ~ResourceBase();

        // C++/CLI finalizer -> Finalize()
        !ResourceBase();

        // Virtual methods for override chain testing
        virtual int Execute(int input);
        virtual String^ GetStatus();
        virtual void OnDisposing();

        // INameable implementation (implicit)
        virtual property String^ DisplayName {
            String^ get();
        };

        // Property with virtual getter
        property int OperationCount {
            int get();
        };

        property bool IsDisposed {
            bool get();
        };

    protected:
        void ThrowIfDisposed();
    };

    // Derived class that overrides virtual methods
    public ref class DoubleProcessor : public ResourceBase, public IProcessor {
    private:
        int _factor;

    public:
        DoubleProcessor(String^ name, int bufferSize, int factor);

        // Override virtual methods
        virtual int Execute(int input) override;
        virtual String^ GetStatus() override;
        virtual void OnDisposing() override;

        // IProcessor implementation (implicit)
        virtual int Process(int value);
        virtual void Reset();
    };

    // Another derived class for testing longer chains
    public ref class AccumulatingProcessor : public DoubleProcessor {
    private:
        int _accumulated;

    public:
        AccumulatingProcessor(String^ name, int bufferSize, int factor);

        // Override again to test 3-level chain
        virtual int Execute(int input) override;
        virtual String^ GetStatus() override;

        property int Accumulated {
            int get();
        };
    };

    // Standalone class that only implements IDisposable (via destructor)
    public ref class SimpleResource {
    private:
        bool _disposed;
        int _value;

    public:
        SimpleResource(int value);
        ~SimpleResource();
        !SimpleResource();

        virtual int GetValue();
        property bool IsDisposed { bool get(); };
    };

    // Native C++ class with callback to managed virtual methods
    // This generates VTableFixup entries for native-to-managed transitions
    class NativeProcessor {
    private:
        int _multiplier;
    public:
        NativeProcessor(int multiplier);
        ~NativeProcessor();

        // Native method that will call managed code via function pointers
        int ProcessNative(int value);
        int GetMultiplier() const;
    };

    // Managed wrapper around NativeProcessor that bridges native/managed worlds
    public ref class NativeBridge {
    private:
        NativeProcessor* _native;
        bool _disposed;

    public:
        NativeBridge(int multiplier);
        ~NativeBridge();
        !NativeBridge();

        // Virtual methods called from native context
        virtual int Transform(int value);
        virtual void OnCompleted(int result);

        // Executes native processing that calls back into managed code
        int ExecuteNative(int input);

        property bool IsDisposed { bool get(); };
        property int LastResult;
    };

    // Interface for event-driven native callbacks
    public interface class INativeCallback {
        void OnData(int data);
        void OnError(String^ message);
        int OnTransform(int input);
    };

    // Class that receives callbacks from native code through interface
    public ref class CallbackReceiver : public INativeCallback {
    private:
        int _dataSum;
        String^ _lastError;
        int _transformCount;

    public:
        CallbackReceiver();

        virtual void OnData(int data);
        virtual void OnError(String^ message);
        virtual int OnTransform(int input);

        property int DataSum { int get(); };
        property String^ LastError { String^ get(); };
        property int TransformCount { int get(); };
    };

    // Helper class to verify behavior from C++/CLI side
    public ref class Verifier {
    public:
        // Test virtual dispatch through base reference
        static String^ TestVirtualDispatch();

        // Test IDisposable / Dispose pattern
        static String^ TestDispose();

        // Test interface dispatch
        static String^ TestInterfaceDispatch();

        // Test 3-level inheritance chain
        static String^ TestInheritanceChain();

        // Test native-to-managed callbacks
        static String^ TestNativeCallback();

        // Test native bridge with virtual methods
        static String^ TestNativeBridge();

        // Run all tests and return result string
        static String^ RunAllTests();
    };
}
