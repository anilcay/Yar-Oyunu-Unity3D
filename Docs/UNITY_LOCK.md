# Unity Kilidi (paralel ajanlar için)

Aynı Unity projesini aynı anda yalnızca **bir** Unity örneği açabilir. Birden fazla ajan Unity kullanırken:

1. Unity çalıştırmadan önce `Logs/unity.lock` dosyasına bak.
   - Dosya yoksa **ve** `Get-Process Unity` boşsa: dosyayı oluştur, içine `<ajan adı> <saat>` yaz, Unity'yi çalıştır.
   - Dosya varsa veya Unity çalışıyorsa: `Start-Sleep -Seconds 60` ile bekle ve tekrar dene. 20 dakikadan uzun süren kilit bayattır; ancak `Get-Process Unity` boşsa kilidi silebilirsin.
2. Unity işin bitince (başarılı ya da başarısız) kilidi **mutlaka sil**.
3. Unity'yi uzun süre meşgul etme: batch işlerini birleştir (derleme + testler tek koşuda).
4. Derleme başka ajanın dosyasındaki hata yüzünden başarısızsa o dosyayı düzeltme. Bekle ve tekrar dene; 15 dakika sonra hâlâ hata varsa raporunda belirt.
5. `.meta` dosyalarını elle silme ya da oluşturma.
