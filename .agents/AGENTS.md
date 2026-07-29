# Aturan Pengembangan FTTH-Basemap_Refraktory

## Desain UI & Layout Frame Collapsible
Setiap kali menambahkan kartu/frame baru (`Border` dengan `Style="{StaticResource CardStyle}"`) di panel UI:
1. Pastikan kartu tersebut selalu dibuat **collapsible** (bisa diciutkan/dibuka).
2. Struktur header harus menggunakan Grid interaktif dengan `Cursor="Hand"`, `MouseDown="BtnToggleCard_Click"`, dan properti `Tag` unik yang diawali dengan `"Card_"`.
3. Contoh pola XAML yang wajib diikuti:
   ```xml
   <Border Style="{StaticResource CardStyle}">
       <StackPanel>
           <Grid Cursor="Hand" MouseDown="BtnToggleCard_Click" Background="Transparent" Tag="Card_NamaKartuBaru" Margin="0,0,0,8">
               <Grid.ColumnDefinitions>
                   <ColumnDefinition Width="*" />
                   <ColumnDefinition Width="Auto" />
               </Grid.ColumnDefinitions>
               <TextBlock Text="Judul Kartu Baru" FontSize="11" FontWeight="Bold" Foreground="{StaticResource AccentBrush}" VerticalAlignment="Center"/>
               <TextBlock Text="▲" FontSize="10" Foreground="#A6ADC8" VerticalAlignment="Center" Grid.Column="1"/>
           </Grid>
           <StackPanel Margin="0,4,0,0">
               <!-- Konten Kartu Baru -->
           </StackPanel>
       </StackPanel>
   </Border>
   ```
4. Prosedur ini menjamin visual tree traversal (`InitializeCardCollapseStates`) dapat mendeteksi kartu baru secara otomatis dan mengingat status buka/tutupnya melalui `settings.xml` secara dinamis tanpa perlu mengubah kode C# di belakang.
