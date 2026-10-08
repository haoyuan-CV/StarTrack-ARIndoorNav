from localize import localize

if __name__ == "__main__":
    prefixes = []

    # 逐行读取前缀，直到遇到 "1"
    while True:
        token = input().strip()
        if token == "1":
            break
        prefixes.append(token)

    # 下一行就是 image_name
    image_name = input().strip()

    # prefixes 始终是 list（无论有几个元素）
    result = localize(prefixes, image_name)

    C = result["C"]
    R = result["R"]

    print(result["image"])
    print("C:", C[0], C[1], C[2])

    for row in R:
        print("R:", row[0], row[1], row[2])
