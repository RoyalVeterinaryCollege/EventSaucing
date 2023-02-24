/****** Object:  Table [dbo].[ItemCounts]    Script Date: 20/04/2022 16:28:33 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ItemCounts]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ItemCounts](
	[Item] [varchar](500) NOT NULL,
	[Count] [int] NOT NULL,
 CONSTRAINT [PK_ItemCounts] PRIMARY KEY CLUSTERED 
(
	[Item] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO

