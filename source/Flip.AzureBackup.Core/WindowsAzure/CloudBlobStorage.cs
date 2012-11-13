﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Flip.AzureBackup.IO;
using Flip.AzureBackup.Logging;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure.StorageClient.Protocol;



namespace Flip.AzureBackup.WindowsAzure
{
	/// <summary>
	/// http://blogs.msdn.com/b/windowsazurestorage/archive/2010/04/11/using-windows-azure-page-blobs-and-how-to-efficiently-upload-and-download-page-blobs.aspx
	/// </summary>
	public sealed class CloudBlobStorage : ICloudBlobStorage
	{
		public CloudBlobStorage(ILogger logger, IFileSystem fileSystem)
		{
			this._logger = logger;
			this._fileSystem = fileSystem;
		}



		public void DownloadFile(CloudBlob blob, string path)
		{
			if (blob.Properties.Length > FileSizeThresholdInBytes)
			{
				DownloadFileInChunks(blob.ToBlockBlob, path);
			}
			else
			{
				blob.DownloadToFile(path);
			}
		}

		public void UploadFile(CloudBlob blob, FileInformation fileInfo)
		{
			if (fileInfo.SizeInBytes > FileSizeThresholdInBytes)
			{
				UploadFileInChunks(blob.ToBlockBlob, fileInfo);
			}
			else
			{
				blob.UploadFile(fileInfo);
			}
		}

		public void UploadFile(CloudBlobContainer blobContainer, FileInformation fileInfo)
		{
			if (fileInfo.SizeInBytes > FileSizeThresholdInBytes)
			{
				var blockBlob = blobContainer.GetBlockBlobReference(fileInfo.RelativePath);
				blockBlob.SetFileLastModifiedUtc(fileInfo.LastWriteTimeUtc, false);
				UploadFileInChunks(blockBlob, fileInfo);
			}
			else
			{
				CloudBlob blob = blobContainer.GetBlobReference(fileInfo.RelativePath);
				blob.UploadFile(fileInfo);
			}
		}



		private void UploadFileInChunks(CloudBlockBlob blockBlob, FileInformation fileInfo)
		{
			List<string> blockIds = new List<string>();

			using (Stream stream = this._fileSystem.GetReadFileStream(fileInfo.FullPath))
			{
				using (BinaryReader reader = new BinaryReader(stream))
				{
					int blockCounter = 0;
					int bytesSent = 0;
					int currentBlockSize = MaxBlockSize;

					while (bytesSent < fileInfo.SizeInBytes)
					{
						if ((bytesSent + currentBlockSize) > fileInfo.SizeInBytes)
						{
							currentBlockSize = (int)fileInfo.SizeInBytes - bytesSent;
						}

						string blockId = blockCounter.ToString().PadLeft(32, '0');
						byte[] bytes = reader.ReadBytes(currentBlockSize);
						using (var memoryStream = new MemoryStream(bytes))
						{
							blockIds.Add(blockId);
							blockBlob.PutBlock(blockId, memoryStream, bytes.GetMD5Hash());
						}
						bytesSent += currentBlockSize;
						blockCounter++;
					}
				}
			}
			//Commit the block list
			blockBlob.PutBlockList(blockIds);
		}

		private void DownloadFileInChunks(CloudBlob blob, string path)
		{
			long blobLength = blob.Properties.Length;
			int rangeStart = 0;
			int currentBlockSize = MaxBlockSize;

			using (Stream fileStream = this._fileSystem.GetWriteFileStream(path))
			{
				while (rangeStart < blobLength)
				{
					if ((rangeStart + currentBlockSize) > blobLength)
					{
						currentBlockSize = (int)blobLength - rangeStart;
					}

					HttpWebRequest blobGetRequest = BlobRequest.Get(blob.Uri, 60, null, null);
					blobGetRequest.Headers.Add("x-ms-range", string.Format(System.Globalization.CultureInfo.InvariantCulture, "bytes={0}-{1}", rangeStart, rangeStart + currentBlockSize - 1));

					// Sign request.
					StorageCredentials credentials = blob.ServiceClient.Credentials;
					credentials.SignRequest(blobGetRequest);

					// Read chunk.
					using (HttpWebResponse response = blobGetRequest.GetResponse() as HttpWebResponse)
					{
						using (Stream stream = response.GetResponseStream())
						{
							stream.CopyTo(fileStream);
						}
					}

					rangeStart += currentBlockSize;
				}
			}
		}



		private readonly ILogger _logger;
		private readonly IFileSystem _fileSystem;
		private static readonly int MaxBlockSize = 4.MBToBytes();
		private static readonly long FileSizeThresholdInBytes = (12L).MBToBytes();
		private const int PageBlobPageFactor = 512;
	}
}