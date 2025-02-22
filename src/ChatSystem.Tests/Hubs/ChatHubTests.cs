using ChatSystem.API.Hubs;
using ChatSystem.Application.DTOs;
using ChatSystem.Application.Interfaces;
using ChatSystem.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace ChatSystem.Tests.Hubs;

public class ChatHubTests
{
    private readonly Mock<IChatService> _mockChatService;
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<HubCallerContext> _mockHubCallerContext;
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly ChatHub _chatHub;

    public ChatHubTests()
    {
        _mockChatService = new Mock<IChatService>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockHubCallerContext = new Mock<HubCallerContext>();
        _mockGroups = new Mock<IGroupManager>();

        _chatHub = new ChatHub(_mockChatService.Object)
        {
            Clients = _mockClients.Object,
            Groups = _mockGroups.Object,
            Context = _mockHubCallerContext.Object
        };

        _mockClients.Setup(x => x.All).Returns(_mockClientProxy.Object);
        _mockClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
    }

    [Fact]
    public async Task SendMessage_ShouldBroadcastToGroups()
    {
        // Arrange
        var chatRoomId = Guid.NewGuid();
        var senderId = "user1";
        var message = "Hello!";

        var chatRoom = new ChatRoomDto
        {
            Id = chatRoomId,
            RequestorId = "user1",
            ListenerId = "user2",
            Status = ChatRoomStatus.Active.ToString()
        };

        var messageDto = new ChatMessageDto
        {
            Id = Guid.NewGuid(),
            ChatRoomId = chatRoomId,
            SenderId = senderId,
            Content = message,
            Timestamp = DateTime.UtcNow
        };

        _mockChatService.Setup(x => x.GetChatRoomByIdAsync(chatRoomId))
            .ReturnsAsync(chatRoom);
        _mockChatService.Setup(x => x.SendMessageAsync(It.IsAny<CreateChatMessageDto>()))
            .ReturnsAsync(messageDto);

        // Act
        var result = await _chatHub.SendMessage(chatRoomId, senderId, message);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message, result.Content);
        _mockClients.Verify(x => x.Group($"user_{chatRoom.RequestorId}"), Times.Once);
        _mockClients.Verify(x => x.Group($"user_{chatRoom.ListenerId}"), Times.Once);
        _mockClientProxy.Verify(x => x.SendCoreAsync("ReceiveMessage", 
            It.Is<object[]>(o => o[0].ToString() == senderId && o[1].ToString() == message), 
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateChatRequest_ShouldNotifyAllClients()
    {
        // Arrange
        var requestorId = "user1";
        var requestMessage = "Can we chat?";
        var chatRoom = new ChatRoomDto
        {
            Id = Guid.NewGuid(),
            RequestorId = requestorId,
            Status = ChatRoomStatus.Pending.ToString(),
            CreatedAt = DateTime.UtcNow
        };

        _mockChatService.Setup(x => x.CreateChatRequestAsync(It.IsAny<CreateChatRoomDto>()))
            .ReturnsAsync(chatRoom);

        // Act
        var result = await _chatHub.CreateChatRequest(requestorId, requestMessage);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(requestorId, result.RequestorId);
        _mockClients.Verify(x => x.All, Times.Once);
        _mockClientProxy.Verify(x => x.SendCoreAsync("NewChatRequest", 
            It.Is<object[]>(o => o[0] == chatRoom), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptChatRequest_ShouldNotifyBothUsers()
    {
        // Arrange
        var chatRoomId = Guid.NewGuid();
        var listenerId = "user2";
        var chatRoom = new ChatRoomDto
        {
            Id = chatRoomId,
            RequestorId = "user1",
            ListenerId = listenerId,
            Status = ChatRoomStatus.Active.ToString()
        };

        _mockChatService.Setup(x => x.AcceptChatRequestAsync(It.IsAny<AcceptChatRequestDto>()))
            .ReturnsAsync(chatRoom);

        // Act
        var result = await _chatHub.AcceptChatRequest(chatRoomId, listenerId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(listenerId, result.ListenerId);
        _mockClients.Verify(x => x.Group($"user_{chatRoom.RequestorId}"), Times.Once);
        _mockClients.Verify(x => x.Group($"user_{chatRoom.ListenerId}"), Times.Once);
        _mockClientProxy.Verify(x => x.SendCoreAsync("ChatAccepted", 
            It.Is<object[]>(o => o[0].ToString() == listenerId), 
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
} 