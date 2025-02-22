using ChatSystem.Application.DTOs;
using ChatSystem.Application.Services;
using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Repositories;
using Moq;
using Xunit;

namespace ChatSystem.Tests.Services;

public class ChatServiceTests
{
    private readonly Mock<IChatRepository> _mockChatRepository;
    private readonly ChatService _chatService;

    public ChatServiceTests()
    {
        _mockChatRepository = new Mock<IChatRepository>();
        _chatService = new ChatService(_mockChatRepository.Object);
    }

    [Fact]
    public async Task CreateChatRequestAsync_ShouldCreateNewChatRoom()
    {
        // Arrange
        var request = new CreateChatRoomDto
        {
            RequestorId = "user1",
            RequestMessage = "Hello, can we chat?"
        };

        var expectedChatRoom = new ChatRoom
        {
            Id = Guid.NewGuid(),
            RequestorId = request.RequestorId,
            Status = ChatRoomStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _mockChatRepository.Setup(x => x.CreateChatRoomAsync(It.IsAny<ChatRoom>()))
            .ReturnsAsync(expectedChatRoom);

        // Act
        var result = await _chatService.CreateChatRequestAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.RequestorId, result.RequestorId);
        Assert.Equal(ChatRoomStatus.Pending.ToString(), result.Status);
        _mockChatRepository.Verify(x => x.CreateChatRoomAsync(It.IsAny<ChatRoom>()), Times.Once);
    }

    [Fact]
    public async Task AcceptChatRequestAsync_ShouldUpdateChatRoomStatus()
    {
        // Arrange
        var chatRoomId = Guid.NewGuid();
        var request = new AcceptChatRequestDto
        {
            ChatRoomId = chatRoomId,
            ListenerId = "user2"
        };

        var existingChatRoom = new ChatRoom
        {
            Id = chatRoomId,
            RequestorId = "user1",
            Status = ChatRoomStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        var updatedChatRoom = new ChatRoom
        {
            Id = chatRoomId,
            RequestorId = "user1",
            ListenerId = "user2",
            Status = ChatRoomStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _mockChatRepository.Setup(x => x.GetChatRoomByIdAsync(chatRoomId))
            .ReturnsAsync(existingChatRoom);
        _mockChatRepository.Setup(x => x.UpdateChatRoomStatusAsync(chatRoomId, ChatRoomStatus.Active))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _chatService.AcceptChatRequestAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.ListenerId, result.ListenerId);
        Assert.Equal(ChatRoomStatus.Active.ToString(), result.Status);
        _mockChatRepository.Verify(x => x.UpdateChatRoomStatusAsync(chatRoomId, ChatRoomStatus.Active), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldCreateNewMessage()
    {
        // Arrange
        var messageDto = new CreateChatMessageDto
        {
            ChatRoomId = Guid.NewGuid(),
            SenderId = "user1",
            Content = "Hello!"
        };

        var chatRoom = new ChatRoom
        {
            Id = messageDto.ChatRoomId,
            RequestorId = "user1",
            ListenerId = "user2",
            Status = ChatRoomStatus.Active
        };

        var expectedMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatRoomId = messageDto.ChatRoomId,
            SenderId = messageDto.SenderId,
            Content = messageDto.Content,
            Timestamp = DateTime.UtcNow
        };

        _mockChatRepository.Setup(x => x.GetChatRoomByIdAsync(messageDto.ChatRoomId))
            .ReturnsAsync(chatRoom);
        _mockChatRepository.Setup(x => x.SaveMessageAsync(It.IsAny<ChatMessage>()))
            .ReturnsAsync(expectedMessage);

        // Act
        var result = await _chatService.SendMessageAsync(messageDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(messageDto.Content, result.Content);
        Assert.Equal(messageDto.SenderId, result.SenderId);
        _mockChatRepository.Verify(x => x.SaveMessageAsync(It.IsAny<ChatMessage>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingChatRequestsAsync_ShouldReturnPendingRequests()
    {
        // Arrange
        var pendingRequests = new List<ChatRoom>
        {
            new ChatRoom { Id = Guid.NewGuid(), RequestorId = "user1", Status = ChatRoomStatus.Pending },
            new ChatRoom { Id = Guid.NewGuid(), RequestorId = "user2", Status = ChatRoomStatus.Pending }
        };

        _mockChatRepository.Setup(x => x.GetPendingChatRoomsAsync())
            .ReturnsAsync(pendingRequests);

        // Act
        var result = await _chatService.GetPendingChatRequestsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.All(result, r => Assert.Equal(ChatRoomStatus.Pending.ToString(), r.Status));
        _mockChatRepository.Verify(x => x.GetPendingChatRoomsAsync(), Times.Once);
    }
} 