// ============================================================================
// Connection Resilience Unit Tests
// ============================================================================
//
// Tests for IBKR connection disconnect/reconnect handling including:
// - Error code 1100: Connectivity lost detection
// - Error code 1101: Connectivity restored with data loss
// - Error code 1102: Connectivity restored with data maintained
// - Connection state tracking (IsConnected property)
// - Event firing for connection state changes
// - Market data resubscription logic
//
// IBKR Error Codes Reference:
// - 1100: Connectivity between IB and TWS has been lost
// - 1101: Connectivity restored, data lost - resubscription required
// - 1102: Connectivity restored, data maintained
//
// ============================================================================

using IdiotProof.Backend.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for connection resilience and automatic reconnection handling.
/// Verifies that the IbWrapper correctly detects connectivity changes
/// and fires appropriate events for the application to respond.
/// </summary>
[TestFixture]
public class ConnectionResilienceTests
{
    private IbWrapper _wrapper = null!;

    [SetUp]
    public void Setup()
    {
        _wrapper = new IbWrapper();
    }

    [TearDown]
    public void TearDown()
    {
        _wrapper?.Dispose();
    }

    // ========================================================================
    // Initial Connection State Tests
    // ========================================================================

    [Test]
    [Description("New wrapper should start with IsConnected = false")]
    public void NewWrapper_IsConnected_IsFalse()
    {
        // Assert
        Assert.That(_wrapper.IsConnected, Is.False);
    }

    [Test]
    [Description("After receiving nextValidId, IsConnected should be true")]
    public void AfterNextValidId_IsConnected_IsTrue()
    {
        // Act - Simulate receiving nextValidId (which happens on successful connection)
        _wrapper.nextValidId(1);

        // Assert
        Assert.That(_wrapper.IsConnected, Is.True);
    }

    // ========================================================================
    // Error Code 1100 - Connectivity Lost Tests
    // ========================================================================

    [Test]
    [Description("Error 1100 should set IsConnected to false")]
    public void Error1100_SetsIsConnected_ToFalse()
    {
        // Arrange - First establish connection
        _wrapper.nextValidId(1);
        Assert.That(_wrapper.IsConnected, Is.True);

        // Act - Simulate connectivity loss
        _wrapper.error(-1, 1100, "Connectivity between IBKR and Trader Workstation has been lost.");

        // Assert
        Assert.That(_wrapper.IsConnected, Is.False);
    }

    [Test]
    [Description("Error 1100 should fire OnConnectionLost event")]
    public void Error1100_FiresOnConnectionLostEvent()
    {
        // Arrange
        bool eventFired = false;
        _wrapper.OnConnectionLost += () => eventFired = true;
        _wrapper.nextValidId(1);

        // Act
        _wrapper.error(-1, 1100, "Connectivity between IBKR and Trader Workstation has been lost.");

        // Assert
        Assert.That(eventFired, Is.True);
    }

    [Test]
    [Description("Error 1100 with extended signature should also fire OnConnectionLost")]
    public void Error1100_ExtendedSignature_FiresOnConnectionLostEvent()
    {
        // Arrange
        bool eventFired = false;
        _wrapper.OnConnectionLost += () => eventFired = true;
        _wrapper.nextValidId(1);

        // Act - Use the extended error signature
        _wrapper.error(-1, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1100,
            "Connectivity between IBKR and Trader Workstation has been lost.", "");

        // Assert
        Assert.That(eventFired, Is.True);
        Assert.That(_wrapper.IsConnected, Is.False);
    }

    // ========================================================================
    // Error Code 1101 - Connectivity Restored (Data Lost) Tests
    // ========================================================================

    [Test]
    [Description("Error 1101 should set IsConnected to true")]
    public void Error1101_SetsIsConnected_ToTrue()
    {
        // Arrange - Simulate connection then loss
        _wrapper.nextValidId(1);
        _wrapper.error(-1, 1100, "Connectivity lost.");
        Assert.That(_wrapper.IsConnected, Is.False);

        // Act - Simulate connectivity restored with data loss
        _wrapper.error(-1, 1101, "Connectivity restored - data lost.");

        // Assert
        Assert.That(_wrapper.IsConnected, Is.True);
    }

    [Test]
    [Description("Error 1101 should fire OnConnectionRestored with dataLost = true")]
    public void Error1101_FiresOnConnectionRestored_WithDataLostTrue()
    {
        // Arrange
        bool eventFired = false;
        bool? dataLostValue = null;
        _wrapper.OnConnectionRestored += (dataLost) =>
        {
            eventFired = true;
            dataLostValue = dataLost;
        };

        // Act
        _wrapper.error(-1, 1101, "Connectivity restored - data lost, need to resubscribe.");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(eventFired, Is.True);
            Assert.That(dataLostValue, Is.True);
        });
    }

    // ========================================================================
    // Error Code 1102 - Connectivity Restored (Data Maintained) Tests
    // ========================================================================

    [Test]
    [Description("Error 1102 should set IsConnected to true")]
    public void Error1102_SetsIsConnected_ToTrue()
    {
        // Arrange - Simulate connection then loss
        _wrapper.nextValidId(1);
        _wrapper.error(-1, 1100, "Connectivity lost.");
        Assert.That(_wrapper.IsConnected, Is.False);

        // Act - Simulate connectivity restored with data maintained
        _wrapper.error(-1, 1102, "Connectivity restored - data maintained.");

        // Assert
        Assert.That(_wrapper.IsConnected, Is.True);
    }

    [Test]
    [Description("Error 1102 should fire OnConnectionRestored with dataLost = false")]
    public void Error1102_FiresOnConnectionRestored_WithDataLostFalse()
    {
        // Arrange
        bool eventFired = false;
        bool? dataLostValue = null;
        _wrapper.OnConnectionRestored += (dataLost) =>
        {
            eventFired = true;
            dataLostValue = dataLost;
        };

        // Act
        _wrapper.error(-1, 1102, "Connectivity restored - data maintained.");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(eventFired, Is.True);
            Assert.That(dataLostValue, Is.False);
        });
    }

    // ========================================================================
    // Connection Closed Tests
    // ========================================================================

    [Test]
    [Description("connectionClosed should set IsConnected to false")]
    public void ConnectionClosed_SetsIsConnected_ToFalse()
    {
        // Arrange
        _wrapper.nextValidId(1);
        Assert.That(_wrapper.IsConnected, Is.True);

        // Act
        _wrapper.connectionClosed();

        // Assert
        Assert.That(_wrapper.IsConnected, Is.False);
    }

    // ========================================================================
    // Full Disconnect/Reconnect Cycle Tests
    // ========================================================================

    [Test]
    [Description("Full cycle: Connect -> Disconnect -> Reconnect (data lost)")]
    public void FullCycle_ConnectDisconnectReconnect_DataLost()
    {
        // Arrange
        var connectionEvents = new List<string>();
        _wrapper.OnConnectionLost += () => connectionEvents.Add("lost");
        _wrapper.OnConnectionRestored += (dataLost) => connectionEvents.Add($"restored:{dataLost}");

        // Act - Simulate full cycle
        _wrapper.nextValidId(1);                                    // Connect
        Assert.That(_wrapper.IsConnected, Is.True);

        _wrapper.error(-1, 1100, "Connectivity lost.");             // Disconnect
        Assert.That(_wrapper.IsConnected, Is.False);

        _wrapper.error(-1, 1101, "Connectivity restored.");         // Reconnect with data loss
        Assert.That(_wrapper.IsConnected, Is.True);

        // Assert
        Assert.That(connectionEvents, Is.EqualTo(new[] { "lost", "restored:True" }));
    }

    [Test]
    [Description("Full cycle: Connect -> Disconnect -> Reconnect (data maintained)")]
    public void FullCycle_ConnectDisconnectReconnect_DataMaintained()
    {
        // Arrange
        var connectionEvents = new List<string>();
        _wrapper.OnConnectionLost += () => connectionEvents.Add("lost");
        _wrapper.OnConnectionRestored += (dataLost) => connectionEvents.Add($"restored:{dataLost}");

        // Act - Simulate full cycle
        _wrapper.nextValidId(1);                                    // Connect
        _wrapper.error(-1, 1100, "Connectivity lost.");             // Disconnect
        _wrapper.error(-1, 1102, "Connectivity restored.");         // Reconnect with data maintained

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_wrapper.IsConnected, Is.True);
            Assert.That(connectionEvents, Is.EqualTo(new[] { "lost", "restored:False" }));
        });
    }

    [Test]
    [Description("Multiple disconnect/reconnect cycles should work correctly")]
    public void MultipleCycles_DisconnectReconnect_WorksCorrectly()
    {
        // Arrange
        int lostCount = 0;
        int restoredCount = 0;
        _wrapper.OnConnectionLost += () => lostCount++;
        _wrapper.OnConnectionRestored += (_) => restoredCount++;

        // Act - Multiple cycles
        _wrapper.nextValidId(1);

        for (int i = 0; i < 3; i++)
        {
            _wrapper.error(-1, 1100, "Connectivity lost.");
            _wrapper.error(-1, 1102, "Connectivity restored.");
        }

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_wrapper.IsConnected, Is.True);
            Assert.That(lostCount, Is.EqualTo(3));
            Assert.That(restoredCount, Is.EqualTo(3));
        });
    }

    // ========================================================================
    // Informational Error Code Suppression Tests
    // ========================================================================

    [Test]
    [Description("Informational error codes (2100-2199) should not affect connection state")]
    public void InformationalErrorCodes_DoNotAffectConnectionState()
    {
        // Arrange
        _wrapper.nextValidId(1);

        // Act - Send informational messages that should be suppressed
        _wrapper.error(-1, 2100, "API client has been unsubscribed from account data.");
        _wrapper.error(-1, 2104, "Market data farm connection is OK.");
        _wrapper.error(-1, 2106, "HMDS data farm connection is OK.");

        // Assert - Connection should still be true
        Assert.That(_wrapper.IsConnected, Is.True);
    }

    // ========================================================================
    // Event Handler Safety Tests
    // ========================================================================

    [Test]
    [Description("OnConnectionLost event should handle no subscribers gracefully")]
    public void OnConnectionLost_NoSubscribers_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => _wrapper.error(-1, 1100, "Connectivity lost."));
    }

    [Test]
    [Description("OnConnectionRestored event should handle no subscribers gracefully")]
    public void OnConnectionRestored_NoSubscribers_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => _wrapper.error(-1, 1101, "Connectivity restored."));
        Assert.DoesNotThrow(() => _wrapper.error(-1, 1102, "Connectivity restored."));
    }
}
