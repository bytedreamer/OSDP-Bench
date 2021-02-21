using Moq;
using MvvmCross.Base;
using MvvmCross.Tests;
using MvvmCross.Views;
using NUnit.Framework;
using OSDPBench.Core.Models;
using OSDPBench.Core.Platforms;
using OSDPBench.Core.ViewModels;

namespace OSDPBench.Core.Tests.ViewModels
{
    public class MainViewModelTests : MvxIoCSupportingTest
    {
        protected MockDispatcher MockDispatcher { get; private set; }

        protected override void AdditionalSetup()
        {
            MockDispatcher = new MockDispatcher();
            Ioc.RegisterSingleton<IMvxViewDispatcher>(MockDispatcher);
            Ioc.RegisterSingleton<IMvxMainThreadAsyncDispatcher>(MockDispatcher);
        }

        [Test]
        public void MainViewModel_InitializedAvailableBaudRates()
        {
            Setup();

            var serialPortConnection = new Mock<ISerialPortConnection>();
            Ioc.RegisterSingleton(serialPortConnection.Object);

            var mainViewModel = Ioc.IoCConstruct<MainViewModel>();

            Assert.AreEqual(6, mainViewModel.AvailableBaudRates.Count);
        }

        [Test]
        public void MainViewModel_InitializedAvailableSerialPorts()
        {
            Setup();

            var serialPortConnection = new Mock<ISerialPortConnection>();
            serialPortConnection.Setup(expression => expression.FindAvailableSerialPorts())
                .ReturnsAsync(new[] {new AvailableSerialPort("id1", "test1", "desc1"), new AvailableSerialPort("id2", "test2", "desc2")});
            Ioc.RegisterSingleton(serialPortConnection.Object);

            var mainViewModel = Ioc.IoCConstruct<MainViewModel>();
            mainViewModel.ViewAppeared();

            Assert.AreEqual(2, mainViewModel.AvailableSerialPorts.Count);
        }
    }
}