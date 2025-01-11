﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace MetaDataStringEditor {
    class MetadataFile : IDisposable {
        public BinaryReader reader;

        private uint stringLiteralOffset;
        private uint stringLiteralCount;
        private long DataInfoPosition;
        private uint stringLiteralDataOffset;
        private uint stringLiteralDataCount;
        private List<StringLiteral> stringLiterals = new List<StringLiteral>();
        public List<byte[]> strBytes = new List<byte[]>();

        public MetadataFile(string fullName) {
            reader = new BinaryReader(File.OpenRead(fullName));

            // 读取文件
            ReadHeader();

            // 读取字符串
            ReadLiteral();
            ReadStrByte();

            Logger.I("Чтение базы завершено");
        }

        private void ReadHeader() {
            Logger.I("读取头部");
            uint vansity = reader.ReadUInt32();
            if (vansity != 0xFAB11BAF) {
                throw new Exception("Неудачная проверка флага");
            }
            int version = reader.ReadInt32();
            stringLiteralOffset = reader.ReadUInt32();      // Расположение области списка не будет изменено в дальнейшем
            stringLiteralCount = reader.ReadUInt32();       // Размер области списка не будет изменен в дальнейшем
            DataInfoPosition = reader.BaseStream.Position;  // Запишите текущее положение.
            stringLiteralDataOffset = reader.ReadUInt32();  // Расположение области данных, которое может быть изменено
            stringLiteralDataCount = reader.ReadUInt32();   // Длина области данных, которая может быть изменена
        }

        private void ReadLiteral() {
            Logger.I("读取Literal");
            ProgressBar.SetMax((int)stringLiteralCount / 8);

            reader.BaseStream.Position = stringLiteralOffset;
            for (int i = 0; i < stringLiteralCount / 8; i++) {
                stringLiterals.Add(new StringLiteral {
                    Length = reader.ReadUInt32(),
                    Offset = reader.ReadUInt32()
                });
                ProgressBar.Report();
            }
        }

        private void ReadStrByte() {
            Logger.I("读取字符串的Bytes");
            ProgressBar.SetMax(stringLiterals.Count);

            for (int i = 0; i < stringLiterals.Count; i++) {
                reader.BaseStream.Position = stringLiteralDataOffset + stringLiterals[i].Offset;
                strBytes.Add(reader.ReadBytes((int)stringLiterals[i].Length));
                ProgressBar.Report();
            }
        }

        public void WriteToNewFile(string fileName) {
            BinaryWriter writer = new BinaryWriter(File.Create(fileName));

            // 先全部复制过去
            reader.BaseStream.Position = 0;
            reader.BaseStream.CopyTo(writer.BaseStream);

            // 更新Literal
            Logger.I("更新Literal");
            ProgressBar.SetMax(stringLiterals.Count);
            writer.BaseStream.Position = stringLiteralOffset;
            uint count = 0;
            for (int i = 0; i < stringLiterals.Count; i++) {

                stringLiterals[i].Offset = count;
                stringLiterals[i].Length = (uint)strBytes[i].Length;

                writer.Write(stringLiterals[i].Length);
                writer.Write(stringLiterals[i].Offset);
                count += stringLiterals[i].Length;

                ProgressBar.Report();
            }

            // 进行一次对齐，不确定是否一定需要，但是Unity是做了，所以还是补上为好
            var tmp = (stringLiteralDataOffset + count) % 4;
            if (tmp != 0) count += 4 - tmp;

            // 检查是否够空间放置
            if (count > stringLiteralDataCount) {
                // 检查数据区后面还有没有别的数据，没有就可以直接延长数据区
                if (stringLiteralDataOffset + stringLiteralDataCount < writer.BaseStream.Length) {
                    // 原有空间不够放，也不能直接延长，所以整体挪到文件尾
                    stringLiteralDataOffset = (uint)writer.BaseStream.Length;
                }
            }
            stringLiteralDataCount = count;

            // Запись в строку
            Logger.I("Обновить строку");
            ProgressBar.SetMax(strBytes.Count);
            writer.BaseStream.Position = stringLiteralDataOffset;
            for (int i = 0; i < strBytes.Count; i++) {
                writer.Write(strBytes[i]);
                ProgressBar.Report();
            }

            // 更新头部
            Logger.I("Обновление заголовка");
            writer.BaseStream.Position = DataInfoPosition;
            writer.Write(stringLiteralDataOffset);
            writer.Write(stringLiteralDataCount);

            Logger.I("Обновления завершены");
            writer.Close();
        }
        
        public void Dispose() {
            reader?.Dispose();
        }
        
        public class StringLiteral {
            public uint Length;
            public uint Offset;
        }
    }
}
